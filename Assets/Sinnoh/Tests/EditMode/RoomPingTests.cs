using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M7 「핑 찍기」(결정 97) · 「소통」(결정 102, 02 §4-8 [가정 M7-67~ · M7-80~]): 방 층 메시지 <see cref="SendSocial"/> → <see cref="SocialMsg"/> — 핑(보드 칸의 누른 자리 · 보드 밖 공개 대상 허용 목록 · 종류 넷) ·
    /// 감정 표현(일곱 줄) · 도장(남의 행동에 셋). 게임 중 좌석이 있는 누구나(남의 턴 · 일시정지 중에도) 보내고 전원이 받는다, 좌석 · 종류마다 연타 제한, 로비 · 틀린 코드 · 허용 목록 밖은 버린다,
    /// 방장은 허용된 칸만으로 새로 만들어 넘긴다(숨은 정보가 새지 않는다), 게임 상태 · 명령 수는 바뀌지 않는다(세이브 · 리플레이에 없다), 상대별 끄기, 전선 위 왕복(코덱) · 프로토콜 판.
    /// </summary>
    public partial class RoomTests
    {
        static SendSocial PingAtCell(int cell, int code = 0) => new SendSocial { Kind = SocialKind.Ping, Code = code, Target = PingTarget.Cell(cell) };

        [Test]
        public void M7_핑_게임중좌석누구나찍으면전원이받고_좌석당5초에3번_보드밖칸과로비는버리며_게임상태와명령수는그대로()
        {
            // 로비에서는 버린다
            var lobbyNet = new Net();
            var lobby = new HostSession(ctx, lobbyNet, Cfg(), 0);
            Assert.IsTrue(lobby.Approve(0, Hi("owner"), 0).Ok);
            Assert.IsTrue(lobby.Approve(1, Hi("g1"), 0).Ok);
            lobby.OnMessage(1, PingAtCell(3), 0);
            lobby.OnMessage(1, new SendSocial { Kind = SocialKind.Emote, Code = 0 }, 0);
            Assert.IsEmpty(lobbyNet.All<SocialMsg>(0), "로비에서는 소통을 버린다");

            var r = new Room(ctx, Cfg(), 3);
            r.Now = 1000;
            ulong hash = r.Host.Game.Hash;
            int commands = r.Host.Game.CommandCount;

            // 좌석 2(지금 답할 것이 있든 없든)가 찍으면 세 좌석 모두 받는다 — 찍은 자신도. 종류 · 누른 자리도 그대로 온다
            r.Host.OnMessage(r.Conn[2], new SendSocial { Kind = SocialKind.Ping, Code = 3, Target = PingTarget.Cell(7).At2(250, -400) }, r.Now);
            for (int seat = 0; seat < 3; seat++)
            {
                var got = r.Net.All<SocialMsg>(r.Conn[seat]);
                Assert.AreEqual(1, got.Count, $"좌석 {seat}");
                Assert.AreEqual((2, SocialKind.Ping, 3, PingAt.Cell, 7), (got[0].From, got[0].Kind, got[0].Code, got[0].Target.At, got[0].Target.N));
                Assert.AreEqual((true, 250, -400), (got[0].Target.Pos, got[0].Target.X, got[0].Target.Y), "누른 자리(칸 반지름 대비 천분율)");
            }

            // 연타 제한: 5초 창에 3번 — 넘는 것은 조용히 버리고, 창이 지나면 다시 된다. 좌석마다 따로 센다(방장은 전선 여유 0.5초만큼 너그럽다)
            r.Host.OnMessage(r.Conn[2], PingAtCell(8), r.Now + 100);
            r.Host.OnMessage(r.Conn[2], PingAtCell(9), r.Now + 200);
            r.Host.OnMessage(r.Conn[2], PingAtCell(10), r.Now + 300);
            r.Host.OnMessage(r.Conn[2], PingAtCell(11), r.Now + PingRules.WindowMs - SocialRate.HostSlackMs - 1);
            Assert.AreEqual(PingRules.Burst, r.Net.All<SocialMsg>(r.Conn[0]).Count, "네 번째부터 버린다");
            r.Host.OnMessage(r.Conn[1], PingAtCell(12), r.Now + 4000);
            Assert.AreEqual(PingRules.Burst + 1, r.Net.All<SocialMsg>(r.Conn[0]).Count, "다른 좌석은 따로");
            Assert.AreEqual(1, r.Net.Last<SocialMsg>(r.Conn[0]).From);
            r.Host.OnMessage(r.Conn[2], PingAtCell(13), r.Now + PingRules.WindowMs);
            Assert.AreEqual((2, 13), (r.Net.Last<SocialMsg>(r.Conn[0]).From, r.Net.Last<SocialMsg>(r.Conn[0]).Target.N), "첫 핑에서 5초가 지났다");
            r.Now += 20000;

            // 보드 밖 칸 · 틀린 종류 · 대상 없음
            int before = r.Net.All<SocialMsg>(r.Conn[0]).Count;
            r.Host.OnMessage(r.Conn[0], PingAtCell(-1), r.Now);
            r.Host.OnMessage(r.Conn[0], PingAtCell(ctx.Board.Count), r.Now);
            r.Host.OnMessage(r.Conn[0], PingAtCell(3, SocialCodes.Ping), r.Now);
            r.Host.OnMessage(r.Conn[0], new SendSocial { Kind = SocialKind.Ping, Code = 0 }, r.Now);
            Assert.AreEqual(before, r.Net.All<SocialMsg>(r.Conn[0]).Count);
            r.Host.OnMessage(r.Conn[0], PingAtCell(ctx.Board.Count - 1), r.Now);
            Assert.AreEqual(before + 1, r.Net.All<SocialMsg>(r.Conn[0]).Count, "마지막 칸 — 버린 것은 연타 제한에 세지 않는다");

            // 일시정지 중에도 찍는다
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = true }, r.Now);
            Assert.IsTrue(r.Host.Paused);
            r.Host.OnMessage(r.Conn[1], PingAtCell(5), r.Now);
            Assert.AreEqual((1, 5), (r.Net.Last<SocialMsg>(r.Conn[2]).From, r.Net.Last<SocialMsg>(r.Conn[2]).Target.N), "일시정지 중");
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = false }, r.Now);

            // 게임 상태 · 명령 수는 그대로(세이브 · 리플레이에 넣지 않는 방 층 메시지)
            Assert.AreEqual((hash, commands), (r.Host.Game.Hash, r.Host.Game.CommandCount));
        }

        [Test]
        public void M7_소통_연타제한표_핑5초3번_감정표현2초1번20초5번_도장1초1번10초5번_전선위왕복_프로토콜판5()
        {
            var rate = new SocialRate();
            Assert.AreEqual(0, rate.WaitMs(0, SocialKind.Ping, 0));
            Assert.IsTrue(rate.TryTake(0, SocialKind.Ping, 0));
            Assert.IsTrue(rate.TryTake(0, SocialKind.Ping, 1000));
            Assert.IsTrue(rate.TryTake(0, SocialKind.Ping, 2000));
            Assert.IsFalse(rate.TryTake(0, SocialKind.Ping, 2500));
            Assert.AreEqual(2500, rate.WaitMs(0, SocialKind.Ping, 2500), "첫 핑이 창을 벗어날 때까지");
            Assert.IsTrue(rate.TryTake(1, SocialKind.Ping, 2500), "좌석마다 따로");
            Assert.IsFalse(rate.TryTake(0, SocialKind.Ping, 4999));
            Assert.IsTrue(rate.TryTake(0, SocialKind.Ping, 5000));
            Assert.IsFalse(rate.TryTake(0, SocialKind.Ping, 5500), "막힌 시도는 세지 않는다 — 창 안은 1000 · 2000 · 5000");
            Assert.IsTrue(rate.TryTake(0, SocialKind.Ping, 6000));

            // 감정 표현: 2초에 1번 + 20초에 5번. 종류마다 따로 센다(핑을 다 써도 감정 표현은 된다)
            Assert.IsTrue(rate.TryTake(0, SocialKind.Emote, 6000));
            Assert.IsFalse(rate.TryTake(0, SocialKind.Emote, 7999));
            Assert.AreEqual(1, rate.WaitMs(0, SocialKind.Emote, 7999));
            for (long t = 8000; t <= 14000; t += 2000) Assert.IsTrue(rate.TryTake(0, SocialKind.Emote, t), t.ToString());
            Assert.IsFalse(rate.TryTake(0, SocialKind.Emote, 16000), "20초 창에 다섯 번(6 · 8 · 10 · 12 · 14초)");
            Assert.AreEqual(10000, rate.WaitMs(0, SocialKind.Emote, 16000));
            Assert.IsTrue(rate.TryTake(0, SocialKind.Emote, 26000));

            // 도장: 1초에 1번 + 10초에 5번
            Assert.IsTrue(rate.TryTake(0, SocialKind.Mark, 0));
            Assert.IsFalse(rate.TryTake(0, SocialKind.Mark, 999));
            for (long t = 1000; t <= 4000; t += 1000) Assert.IsTrue(rate.TryTake(0, SocialKind.Mark, t), t.ToString());
            Assert.IsFalse(rate.TryTake(0, SocialKind.Mark, 5000));
            Assert.IsTrue(rate.TryTake(0, SocialKind.Mark, 10000));
            rate.Clear();
            Assert.AreEqual(0, rate.WaitMs(0, SocialKind.Mark, 10000));

            // 방장은 같은 표를 전선 여유(0.5초)만큼 너그럽게 본다 — 화면이 통과시킨 것을 방장이 버리지 않는다
            var host = new SocialRate(SocialRate.HostSlackMs);
            Assert.IsTrue(host.TryTake(0, SocialKind.Emote, 0));
            Assert.IsFalse(host.TryTake(0, SocialKind.Emote, 1499));
            Assert.IsTrue(host.TryTake(0, SocialKind.Emote, 1500));
            Assert.AreEqual((4, 7, 3), (SocialCodes.Of(SocialKind.Ping), SocialCodes.Of(SocialKind.Emote), SocialCodes.Of(SocialKind.Mark)), "핑 넷 · 감정 표현 일곱 · 도장 셋");

            Assert.GreaterOrEqual(Protocol.Version, 6, "소통 메시지로 판을 올렸다 — 앞 판 손님은 E_VERSION");
            var up = MessageCodec.Encode(new SendSocial { Kind = SocialKind.Ping, Code = 2, Target = PingTarget.Spot(8, 3, 1).At2(120, 880) }, 7);
            Assert.IsTrue(MessageCodec.TryDecodeRoom(up, out var room, out int seq, out var error), error);
            var sent = (SendSocial)room;
            Assert.AreEqual((SocialKind.Ping, 2, 7), (sent.Kind, sent.Code, seq));
            Assert.AreEqual((PingAt.Spot, 8, 3, 1, true, 120, 880), (sent.Target.At, sent.Target.N, sent.Target.K, sent.Target.Seat, sent.Target.Pos, sent.Target.X, sent.Target.Y));
            var down = MessageCodec.Encode(new SocialMsg { From = 3, Kind = SocialKind.Mark, Code = 1, Seq = 42, Owner = 0 }, 9);
            Assert.IsTrue(MessageCodec.TryDecodeServer(down, out var server, out error), error);
            var relay = (SocialMsg)server;
            Assert.AreEqual((3, SocialKind.Mark, 1, 42, 0), (relay.From, relay.Kind, relay.Code, relay.Seq, relay.Owner));
            Assert.IsNull(relay.Target);
            Assert.AreEqual(down, MessageCodec.Encode(server, 9), "되읽어 다시 쓰면 같은 바이트");
            var ping = MessageCodec.Encode(new SocialMsg { From = 5, Kind = SocialKind.Ping, Code = 3, Target = PingTarget.Cell(350).At2(-2000, 2000) }, 9);
            Assert.Less(System.Text.Encoding.UTF8.GetByteCount(ping), 200, "핑 하나는 작다(Relay 사용량 — 좌석당 5초에 3번까지)");
        }

        [Test]
        public void M7_소통_핑대상은허용목록으로새로만든다_내것은좌석까지_숨은정보는실려와도새지않는다()
        {
            // 허용 목록: 요소 이름 → 줄(번호가 붙는 줄은 머리 뒤가 숫자뿐일 때만)
            Assert.AreEqual((8, 2), (PingSpots.Find("board-hud-mon-2", out int k), k), "아래 판의 파티 칸");
            Assert.AreEqual(-1, PingSpots.Find("board-hud-mon-2-name", out _), "칸 안의 글은 칸으로 올라가서 찾는다");
            Assert.AreEqual(-1, PingSpots.Find("board-hud-mon-9", out _), "번호 범위 밖");
            Assert.AreEqual((PingSpots.LogRow, 137), (PingSpots.Find("log-line-137", out k), k), "기록 줄 = 행동 번호");
            Assert.AreEqual(SpotScope.Log, PingSpots.Rows[PingSpots.LogRow].Scope);
            Assert.AreEqual(17, PingSpots.Find("battle-info-foe", out _));
            Assert.AreEqual(17, PingSpots.Find("battle-info-me", out _));
            Assert.AreEqual((14, 3), (PingSpots.Find("encounter-card-3", out k), k));
            Assert.AreEqual("encounter-card-3", PingSpots.NameOf(14, 3));
            Assert.AreEqual(13, PingSpots.Find("act-card-panel", out _));
            // 숨은 것이 든 창(운명 손패) · 고르는 창 · 단추는 목록에 없다 — 손패 창은 좌석까지만
            foreach (var hidden in new[] { "board-fate-panel", "board-fate-card-0", "board-shrine-card-1", "board-trade-panel", "encounter-pick-1", "board-hud-move-go", "battle-fate-pass", "" })
                Assert.AreEqual(-1, PingSpots.Find(hidden, out _), hidden);
            CollectionAssert.Contains(PingSpots.ToSeat, "board-fate-panel");
            var names = new HashSet<string>();
            foreach (var row in PingSpots.Rows) Assert.IsTrue(names.Add(row.Name), "겹친 줄: " + row.Name);

            const int seats = 3, cells = 351, next = 40;
            // 보드 칸: 누른 자리는 범위 안으로 자르고, 다른 칸(좌석 · 번호)은 버린다
            var cell = PingSpots.Norm(new PingTarget { At = PingAt.Cell, N = 7, Seat = 2, K = 99, Pos = true, X = 99999, Y = -99999 }, 1, seats, cells, next);
            Assert.AreEqual((PingAt.Cell, 7, -1, -1, true, PingRules.CellMax, -PingRules.CellMax), (cell.At, cell.N, cell.Seat, cell.K, cell.Pos, cell.X, cell.Y));
            Assert.IsNull(PingSpots.Norm(PingTarget.Cell(cells), 1, seats, cells, next));
            // 좌석
            Assert.AreEqual(2, PingSpots.Norm(new PingTarget { At = PingAt.Seat, Seat = 2, N = 55, K = 7 }, 1, seats, cells, next).Seat);
            Assert.AreEqual(-1, PingSpots.Norm(new PingTarget { At = PingAt.Seat, Seat = 2, N = 55, K = 7 }, 1, seats, cells, next).N, "좌석 대상에 실려 온 번호(손패 카드 번호 따위)는 넘기지 않는다");
            Assert.IsNull(PingSpots.Norm(PingTarget.OfSeat(seats), 1, seats, cells, next));
            // 내 것(아래 판): 주인은 늘 보낸 좌석 — 남의 좌석을 적어 보내도 바뀐다
            var mine = PingSpots.Norm(PingTarget.Spot(8, 2, 0).At2(1500, -3), 1, seats, cells, next);
            Assert.AreEqual((PingAt.Spot, 8, 2, 1, 1000, 0), (mine.At, mine.N, mine.K, mine.Seat, mine.X, mine.Y));
            Assert.IsNull(PingSpots.Norm(PingTarget.Spot(8, 6, 1), 1, seats, cells, next), "파티 칸은 여섯");
            // 모두가 보는 요소: 좌석 · 번호를 싣지 않는다
            var shared = PingSpots.Norm(PingTarget.Spot(13, 5, 2), 1, seats, cells, next);
            Assert.AreEqual((13, -1, -1, false), (shared.N, shared.K, shared.Seat, shared.Pos));
            // 어느 좌석의 창(정비 판 · 트레이너 카드): 그 좌석이 있어야 한다
            Assert.AreEqual(2, PingSpots.Norm(PingTarget.Spot(15, -1, 2), 1, seats, cells, next).Seat);
            Assert.IsNull(PingSpots.Norm(PingTarget.Spot(15, -1, -1), 1, seats, cells, next));
            // 배틀의 쪽은 0·1, 기록 줄은 지금까지의 행동 번호만
            Assert.AreEqual(1, PingSpots.Norm(PingTarget.Spot(17, 1), 1, seats, cells, next).K);
            Assert.IsNull(PingSpots.Norm(PingTarget.Spot(17, 2), 1, seats, cells, next));
            Assert.AreEqual(39, PingSpots.Norm(PingTarget.Spot(PingSpots.LogRow, 39), 1, seats, cells, next).K);
            Assert.IsNull(PingSpots.Norm(PingTarget.Spot(PingSpots.LogRow, 40), 1, seats, cells, next), "아직 없는 행동");
            // 목록 밖 줄 · 모르는 갈래
            Assert.IsNull(PingSpots.Norm(PingTarget.Spot(PingSpots.Rows.Length), 1, seats, cells, next));
            Assert.IsNull(PingSpots.Norm(PingTarget.Spot(-1), 1, seats, cells, next));
            Assert.IsNull(PingSpots.Norm(new PingTarget { At = (PingAt)9, N = 1 }, 1, seats, cells, next));
            Assert.IsNull(PingSpots.Norm(null, 1, seats, cells, next));

            // 방 전체로: 손님이 감정 표현 · 도장에 대상(손패 카드 번호를 흉내 낸 값)을 실어 보내도 넘어가지 않고, 핑의 대상은 방장이 새로 만든 것이다
            var r = new Room(ctx, Cfg(), 3);
            r.Now = 1000;
            var leak = PingTarget.Spot(8, 4, 2).At2(10, 20);
            r.Host.OnMessage(r.Conn[1], new SendSocial { Kind = SocialKind.Emote, Code = 5, Target = leak, Seq = 3, Owner = 2 }, r.Now);
            var emote = r.Net.Last<SocialMsg>(r.Conn[0]);
            Assert.AreEqual((1, SocialKind.Emote, 5), (emote.From, emote.Kind, emote.Code));
            Assert.IsNull(emote.Target, "감정 표현은 대상이 없다");
            Assert.AreEqual((-1, -1), (emote.Seq, emote.Owner));
            var sentPing = new SendSocial { Kind = SocialKind.Ping, Code = 1, Target = leak, Seq = 3, Owner = 2 };
            r.Host.OnMessage(r.Conn[1], sentPing, r.Now);
            var ping = r.Net.Last<SocialMsg>(r.Conn[2]);
            Assert.AreNotSame(leak, ping.Target);
            Assert.AreEqual((8, 4, 1), (ping.Target.N, ping.Target.K, ping.Target.Seat), "내 파티 칸의 주인은 보낸 좌석(1)");
            Assert.AreEqual((-1, -1), (ping.Seq, ping.Owner));
        }

        [Test]
        public void M7_소통_감정표현일곱줄과도장셋_자기행동에는못찍고없는행동은버린다_연타제한_일시정지중에도_게임상태는그대로_상대별끄기()
        {
            var r = new Room(ctx, Cfg(), 3);
            r.Now = 1000;
            ulong hash = r.Host.Game.Hash;
            int commands = r.Host.Game.CommandCount, next = r.Host.Game.NextSeq;
            Assert.Greater(next, 0, "게임 시작이 행동을 냈다");

            // 감정 표현: 일곱 줄(0~6) — 전원이 받는다(보낸 자신도), 2초에 1번
            r.Host.OnMessage(r.Conn[0], new SendSocial { Kind = SocialKind.Emote, Code = SocialCodes.Emote - 1 }, r.Now);
            for (int seat = 0; seat < 3; seat++) Assert.AreEqual((0, SocialKind.Emote, 6), (r.Net.Last<SocialMsg>(r.Conn[seat]).From, r.Net.Last<SocialMsg>(r.Conn[seat]).Kind, r.Net.Last<SocialMsg>(r.Conn[seat]).Code), $"좌석 {seat}");
            int count = r.Net.All<SocialMsg>(r.Conn[1]).Count;
            r.Host.OnMessage(r.Conn[0], new SendSocial { Kind = SocialKind.Emote, Code = 0 }, r.Now + 1000);
            r.Host.OnMessage(r.Conn[0], new SendSocial { Kind = SocialKind.Emote, Code = SocialCodes.Emote }, r.Now + 3000);
            r.Host.OnMessage(r.Conn[0], new SendSocial { Kind = SocialKind.Emote, Code = -1 }, r.Now + 3000);
            r.Host.OnMessage(r.Conn[0], new SendSocial { Kind = (SocialKind)7, Code = 0 }, r.Now + 3000);
            Assert.AreEqual(count, r.Net.All<SocialMsg>(r.Conn[1]).Count, "2초 안의 둘째 · 없는 줄 · 모르는 종류는 버린다");
            r.Host.OnMessage(r.Conn[2], new SendSocial { Kind = SocialKind.Emote, Code = 1 }, r.Now + 1000);
            Assert.AreEqual(count + 1, r.Net.All<SocialMsg>(r.Conn[1]).Count, "다른 좌석은 따로");
            r.Now += 30000;

            // 도장: 남의 행동(행동 번호 + 그 줄의 주인 좌석)에 셋 가운데 하나 — 자기 행동 · 없는 행동 · 없는 좌석은 버린다
            count = r.Net.All<SocialMsg>(r.Conn[1]).Count;
            r.Host.OnMessage(r.Conn[1], new SendSocial { Kind = SocialKind.Mark, Code = 1, Seq = next - 1, Owner = 0 }, r.Now);
            var mark = r.Net.Last<SocialMsg>(r.Conn[2]);
            Assert.AreEqual((1, SocialKind.Mark, 1, next - 1, 0), (mark.From, mark.Kind, mark.Code, mark.Seq, mark.Owner));
            Assert.AreEqual(count + 1, r.Net.All<SocialMsg>(r.Conn[1]).Count);
            r.Host.OnMessage(r.Conn[1], new SendSocial { Kind = SocialKind.Mark, Code = 0, Seq = next - 1, Owner = 1 }, r.Now + 2000);
            r.Host.OnMessage(r.Conn[1], new SendSocial { Kind = SocialKind.Mark, Code = 0, Seq = next, Owner = 0 }, r.Now + 2000);
            r.Host.OnMessage(r.Conn[1], new SendSocial { Kind = SocialKind.Mark, Code = 0, Seq = -1, Owner = 0 }, r.Now + 2000);
            r.Host.OnMessage(r.Conn[1], new SendSocial { Kind = SocialKind.Mark, Code = 0, Seq = 0, Owner = 3 }, r.Now + 2000);
            r.Host.OnMessage(r.Conn[1], new SendSocial { Kind = SocialKind.Mark, Code = SocialCodes.Mark, Seq = 0, Owner = 0 }, r.Now + 2000);
            Assert.AreEqual(count + 1, r.Net.All<SocialMsg>(r.Conn[1]).Count, "내 행동 · 아직 없는 행동 · 없는 좌석 · 없는 도장");
            // 같은 행동에 다시 찍으면(바꿈) 그대로 넘긴다 — 한 사람 1개는 받는 화면이 갈음한다. 1초에 1번
            r.Host.OnMessage(r.Conn[1], new SendSocial { Kind = SocialKind.Mark, Code = 2, Seq = next - 1, Owner = 0 }, r.Now + 2000);
            r.Host.OnMessage(r.Conn[1], new SendSocial { Kind = SocialKind.Mark, Code = 0, Seq = next - 1, Owner = 0 }, r.Now + 2100);
            Assert.AreEqual(count + 2, r.Net.All<SocialMsg>(r.Conn[1]).Count);
            Assert.AreEqual(2, r.Net.Last<SocialMsg>(r.Conn[1]).Code);

            // 일시정지 중에도 · 게임 상태 · 명령 수 · 행동 수는 그대로
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = true }, r.Now + 5000);
            Assert.IsTrue(r.Host.Paused);
            r.Host.OnMessage(r.Conn[2], new SendSocial { Kind = SocialKind.Emote, Code = 3 }, r.Now + 5000);
            Assert.AreEqual((2, 3), (r.Net.Last<SocialMsg>(r.Conn[0]).From, r.Net.Last<SocialMsg>(r.Conn[0]).Code), "일시정지 중");
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = false }, r.Now + 5000);
            Assert.AreEqual((hash, commands, next), (r.Host.Game.Hash, r.Host.Game.CommandCount, r.Host.Game.NextSeq));

            // 상대별 끄기(받는 쪽 거르기): 반응(감정 표현 · 도장)과 핑을 좌석마다 따로 숨긴다 — 내 것은 늘 보인다
            var mutes = new SocialMutes();
            var emoteFrom2 = new SocialMsg { From = 2, Kind = SocialKind.Emote, Code = 0 };
            var markFrom2 = new SocialMsg { From = 2, Kind = SocialKind.Mark, Code = 0, Seq = 1, Owner = 0 };
            var pingFrom2 = new SocialMsg { From = 2, Kind = SocialKind.Ping, Code = 0, Target = PingTarget.Cell(1) };
            Assert.IsTrue(mutes.Shows(emoteFrom2, 0) && mutes.Shows(markFrom2, 0) && mutes.Shows(pingFrom2, 0));
            mutes.Set(2, false, true);
            Assert.IsFalse(mutes.Shows(emoteFrom2, 0), "좌석 2의 감정 표현");
            Assert.IsFalse(mutes.Shows(markFrom2, 0), "좌석 2의 도장");
            Assert.IsTrue(mutes.Shows(pingFrom2, 0), "핑은 따로");
            Assert.IsTrue(mutes.Shows(new SocialMsg { From = 1, Kind = SocialKind.Emote }, 0), "다른 좌석은 그대로");
            Assert.IsTrue(mutes.Shows(emoteFrom2, 2), "내 것은 늘 보인다");
            mutes.Set(2, true, true);
            Assert.IsFalse(mutes.Shows(pingFrom2, 0));
            mutes.Set(2, false, false);
            Assert.IsTrue(mutes.Shows(emoteFrom2, 0));
            Assert.IsFalse(mutes.Shows(pingFrom2, 0));
            mutes.Clear();
            Assert.IsTrue(mutes.Shows(pingFrom2, 0));
            Assert.IsFalse(mutes.Shows(null, 0));
        }
    }
}
