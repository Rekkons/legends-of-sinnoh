using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 결정 149 · 154 무르기 · 되돌리기의 방 몫(Docs/Design/undo.md §2 · §5 「EditMode — 방」, 조각 C 1차 — 규칙 핵 없이 도는 것): 메시지 코덱 왕복 ·
    /// 끈 방의 무르기 → UNDO_DENIED · 방장 아닌 좌석의 목록 요청 · 되돌리기는 무시 · 방장의 목록 요청 → 방장에게만 빈 목록 ·
    /// 되돌림 표지가 실린 갱신은 늘 스냅샷(스트림 Reset은 Flush가 표지를 보고 한다) · 좌석마다 제안 손보기(일시정지 · 목록은 방장만).
    /// 2차(조각 A 합친 뒤): 손님 무르기 → 모두에게 스냅샷 + 표지 · 낡은 To · 일시정지 · 방장 되돌리기의 시계 · 뱅크 · 라운드 충전 · 결과 뒤 되돌림 · Epoch 거절 · 비공개 정보.
    /// </summary>
    public partial class RoomTests
    {
        [Test]
        public void M10_무르기_방_코덱왕복()
        {
            // 손님 → 방장
            Assert.IsTrue(MessageCodec.TryDecodeRoom(MessageCodec.Encode(new UndoStep { To = 42 }, 3), out var m, out int seq, out string error), error);
            Assert.AreEqual((42, 3), (((UndoStep)m).To, seq));
            Assert.IsTrue(MessageCodec.TryDecodeRoom(MessageCodec.Encode(new RequestUndoPoints(), 4), out m, out _, out error), error);
            Assert.IsInstanceOf<RequestUndoPoints>(m);
            Assert.IsTrue(MessageCodec.TryDecodeRoom(MessageCodec.Encode(new RewindTo { To = 17 }, 5), out m, out _, out error), error);
            Assert.AreEqual(17, ((RewindTo)m).To);
            Assert.IsTrue(MessageCodec.TryDecodeRoom(MessageCodec.Encode(new Submit { Command = Command.AnswerTo(9, ChoiceAnswer.Of(AnswerKind.Decline)), Epoch = 2 }, 6), out m, out _, out error), error);
            var sub = (Submit)m;
            Assert.AreEqual((2, 9), (sub.Epoch, sub.Command.ChoiceId), "Submit은 되돌림 횟수를 싣는다");

            // 방장 → 손님: 되돌리기 목록
            var points = new UndoPointsMsg();
            points.Points.Add(new UndoPoint { To = 30, Round = 2, TurnId = 5, Seat = 1, Current = true, Deeds = { new UndoDeed { Kind = UndoDeedKind.Caught, Def = 12 }, new UndoDeed { Kind = UndoDeedKind.Moved } } });
            Assert.IsTrue(MessageCodec.TryDecodeServer(MessageCodec.Encode(points), out var s, out error), error);
            var p = ((UndoPointsMsg)s).Points;
            Assert.AreEqual(1, p.Count);
            Assert.AreEqual((30, 2, 5, 1, true), (p[0].To, p[0].Round, p[0].TurnId, p[0].Seat, p[0].Current));
            Assert.AreEqual(2, p[0].Deeds.Count);
            Assert.AreEqual((UndoDeedKind.Caught, 12, -1), (p[0].Deeds[0].Kind, p[0].Deeds[0].Def, p[0].Deeds[0].Arg));

            // 방장 → 손님: 스냅샷의 제안 · 되돌림 표지(손님 스트림이 GameUpdate로 되살린다)
            var r = new Room(ctx, Cfg(), 2);
            var u = new GameUpdate
            {
                Version = 3, View = r.Host.Game.ViewFor(1), Events = new List<GameEvent>(),
                Undo = new UndoOffer { Mode = UndoMode.Free, StepTo = 8, Block = UndoBlock.None, HostList = true, Epoch = 1 },
                Rewind = new RewindNote { Kind = RewindKind.Host, By = 0, To = 8, Round = 2, TurnSeat = 1, NextSeq = 40, Epoch = 1 },
            };
            Assert.IsTrue(MessageCodec.TryDecodeServer(MessageCodec.Encode(new SeatStream().Encode(u)), out s, out error), error);
            Assert.IsInstanceOf<GameSnapshot>(s);
            var back = new StreamReceiver().Receive(s);
            Assert.IsNotNull(back);
            Assert.AreEqual((UndoMode.Free, 8, UndoBlock.None, true, 1), (back.Undo.Mode, back.Undo.StepTo, back.Undo.Block, back.Undo.HostList, back.Undo.Epoch));
            var n = back.Rewind;
            Assert.AreEqual((RewindKind.Host, 0, 8, 2, 1, 40, 1), (n.Kind, n.By, n.To, n.Round, n.TurnSeat, n.NextSeq, n.Epoch));
        }

        [Test]
        public void M10_무르기_방_끈방에서무르기는UNDO_DENIED_되돌리기도거절()
        {
            Assert.AreEqual(UndoMode.Off, new RoomSettings().Undo, "새 방 기본 = 끔(결정 154 ①)");
            var r = new Room(ctx, Cfg(), 3);
            int g = ToGuestMapAction(r);
            int before = r.Host.Game.CommandCount;
            Assert.IsNull(r.Net.Last<GameUpdate>(r.Conn[g]).Undo, "끈 방 갱신에는 제안이 없다");

            r.Host.OnMessage(r.Conn[g], new UndoStep { To = before - 1 }, r.Now);
            Assert.AreEqual(1, r.Notices(g, NetCode.UndoDenied).Count, "무른 좌석에게만");
            for (int s = 0; s < r.Host.SeatCount; s++)
            {
                if (s != g) Assert.AreEqual(0, r.Notices(s, NetCode.UndoDenied).Count, $"좌석 {s}");
                Assert.AreEqual(0, r.Notices(s, NetCode.Undone).Count, $"좌석 {s}");
            }

            // 방장의 되돌리기도 끈 방(자유가 아님)에서는 거절 — 방장에게만
            r.Host.OnMessage(r.Conn[0], new RewindTo { To = 0 }, r.Now);
            Assert.AreEqual(1, r.Notices(0, NetCode.UndoDenied).Count);
            Assert.AreEqual(0, r.Notices(g, NetCode.Rewound).Count);
            Assert.AreEqual(before, r.Host.Game.CommandCount, "판은 그대로");
            Assert.AreEqual(0, r.Host.Game.Epoch);
        }

        [Test]
        public void M10_무르기_방_방장아닌좌석의목록요청과되돌리기는무시()
        {
            var r = new Room(ctx, Cfg(new RoomSettings { Undo = UndoMode.Free }), 3);
            int g = ToGuestMapAction(r);
            int before = r.Host.Game.CommandCount;

            r.Host.OnMessage(r.Conn[g], new RequestUndoPoints(), r.Now);
            r.Host.OnMessage(r.Conn[g], new RewindTo { To = 0 }, r.Now);
            for (int s = 0; s < r.Host.SeatCount; s++)
            {
                Assert.AreEqual(0, r.Net.All<UndoPointsMsg>(r.Conn[s]).Count, $"좌석 {s}: 목록은 방장만 받는다");
                Assert.AreEqual(0, r.Notices(s, NetCode.UndoDenied).Count, $"좌석 {s}: 거절 알림도 없이 무시");
                Assert.AreEqual(0, r.Notices(s, NetCode.Rewound).Count, $"좌석 {s}");
            }
            Assert.AreEqual(before, r.Host.Game.CommandCount);
            Assert.AreEqual(0, r.Host.Game.Epoch);
        }

        [Test]
        public void M10_무르기_방_방장의목록요청은방장에게만_자유가아니면빈목록()
        {
            var r = new Room(ctx, Cfg(new RoomSettings { Undo = UndoMode.Turn }), 3);
            ToGuestMapAction(r);
            r.Host.OnMessage(r.Conn[0], new RequestUndoPoints(), r.Now);
            var got = r.Net.All<UndoPointsMsg>(r.Conn[0]);
            Assert.AreEqual(1, got.Count);
            Assert.IsNotNull(got[0].Points);
            CollectionAssert.IsEmpty(got[0].Points, "내 턴 무르기 방에는 되돌리기 목록이 없다");
            for (int s = 1; s < r.Host.SeatCount; s++) Assert.AreEqual(0, r.Net.All<UndoPointsMsg>(r.Conn[s]).Count, $"좌석 {s}");
        }

        [Test]
        public void M10_무르기_방_되돌림표지갱신은늘스냅샷_제안은좌석마다손본다()
        {
            var r = new Room(ctx, Cfg(), 3);
            ToGuestMapAction(r);
            var core = (ISessionLink)r.Host; // 핵이 좌석에 갱신을 내는 길 — 되돌림 갱신은 조각 A가 내므로 여기서는 같은 길로 직접 넣는다
            var rx = r.Net.Streams[r.Conn[1]];
            Assert.IsTrue(rx.Snapshots >= 1 && rx.Deltas >= 1, "게임 시작 스냅샷 뒤 델타");
            int snaps = rx.Snapshots, deltas = rx.Deltas;
            GameUpdate Make(int seat, int version, RewindNote note) => new GameUpdate
            {
                Version = version, View = r.Host.Game.ViewFor(seat), Events = new List<GameEvent>(), Rewind = note,
                Undo = new UndoOffer { Mode = UndoMode.Free, StepTo = 5, Block = UndoBlock.None, HostList = true },
            };

            // 표지 없는 갱신 = 델타 · 목록 표시는 방장만
            core.Send(1, Make(1, 1000, null));
            core.Send(0, Make(0, 1000, null));
            r.Host.Tick(r.Now);
            Assert.AreEqual((snaps, deltas + 1), (rx.Snapshots, rx.Deltas));
            var guest = r.Net.Last<GameUpdate>(r.Conn[1]).Undo;
            Assert.AreEqual((5, UndoBlock.None, false), (guest.StepTo, guest.Block, guest.HostList), "손님은 되돌리기 목록을 열지 못한다");
            Assert.IsTrue(r.Net.Last<GameUpdate>(r.Conn[0]).Undo.HostList, "방장은 연다");

            // 되돌림 표지가 실린 갱신 = 스트림 기억이 있어도 스냅샷
            core.Send(1, Make(1, 1001, new RewindNote { Kind = RewindKind.Host, By = 0, To = 7, Epoch = 1 }));
            r.Host.Tick(r.Now);
            Assert.AreEqual((snaps + 1, deltas + 1), (rx.Snapshots, rx.Deltas), "되돌림 갱신은 델타 기준을 믿지 않는다");
            Assert.AreEqual(7, r.Net.Last<GameUpdate>(r.Conn[1]).Rewind.To);
            // 그 다음 갱신은 다시 델타(새 기준 = 스냅샷)
            core.Send(1, Make(1, 1002, null));
            r.Host.Tick(r.Now);
            Assert.AreEqual((snaps + 1, deltas + 2), (rx.Snapshots, rx.Deltas));

            // 일시정지 중 = 못 함(Paused) · 목록 표시는 그대로
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = true }, r.Now);
            Assert.IsTrue(r.Host.Paused);
            core.Send(0, Make(0, 1003, null));
            r.Host.Tick(r.Now);
            var host = r.Net.Last<GameUpdate>(r.Conn[0]).Undo;
            Assert.AreEqual((-1, UndoBlock.Paused, true), (host.StepTo, host.Block, host.HostList));

            // 일시정지 중의 무르기 요청 = UNDO_DENIED(핵에 묻지 않는다)
            r.Host.OnMessage(r.Conn[1], new UndoStep { To = 5 }, r.Now);
            Assert.AreEqual(1, r.Notices(1, NetCode.UndoDenied).Count);
        }

        // ── 2차: 핵(조각 A)과 함께 도는 흐름 ──

        static RoomSettings Free => new RoomSettings { Undo = UndoMode.Free };

        /// <summary>맵 행동에서 이번 턴에 아직 걷지 않았으면 걷는다(선택 Id로 고른 하나), 그 밖은 Default — 무를 거리가 생긴다. 상태 없는 고르기라 되돌린 뒤 같은 선택이면 같은 답.</summary>
        ChoiceAnswer Walk(GameState v, PendingChoice c)
        {
            if (c.Kind != ChoiceKind.MapAction || v.Seats[c.Seat].Moved) return null;
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(v, ctx, c, legal);
            var moves = legal.FindAll(a => a.Kind == AnswerKind.Move);
            return moves.Count == 0 ? null : moves[c.Id % moves.Count];
        }

        /// <summary>봇 고르기(Playout.BotAgent — 선택 Id로 정한 굴림이라 되돌린 뒤 같은 선택이면 같은 답).</summary>
        Func<GameState, PendingChoice, ChoiceAnswer> Bot(ulong seed)
        {
            var bot = new Playout.BotAgent(seed);
            return (v, c) => bot.Decide(new AgentInput { View = v, Choice = c, Ctx = ctx, PublicLog = new List<GameEvent>() }, new AgentBudget { Seed = (ulong)c.Id * 131 + (ulong)c.Seat }).Answer;
        }

        /// <summary>걷고 난 손님 좌석의 맵 행동(무를 수 있는 때 — 그 좌석 마지막 갱신의 StepTo ≥ 0)까지. 돌려주는 값 = 그 좌석.</summary>
        int ToUndoableGuest(Room r)
        {
            int g = -1;
            r.Until(() =>
            {
                for (int s = 1; s < r.Host.SeatCount; s++)
                {
                    var o = r.Net.Last<GameUpdate>(r.Conn[s])?.Undo;
                    if (o != null && o.StepTo >= 0 && r.Mine(s)?.Kind == ChoiceKind.MapAction)
                    {
                        g = s;
                        return true;
                    }
                }
                return false;
            }, Walk);
            r.Now = r.Host.AnimEndMs;
            r.Host.Tick(r.Now);
            return g;
        }

        static UndoOffer OfferOf(Room r, int seat) => r.Net.Last<GameUpdate>(r.Conn[seat]).Undo;

        static int[] SnapshotsOf(Room r)
        {
            var n = new int[r.Host.SeatCount];
            for (int s = 0; s < n.Length; s++) n[s] = r.Net.Streams[r.Conn[s]].Snapshots;
            return n;
        }

        /// <summary>모든 좌석이 되돌림 표지가 실린 스냅샷을 하나씩 더 받았고, 지금 보기 = 호스트 ViewFor, 알림(code · Seat = by)이 하나씩.</summary>
        static RewindNote AllGotRewind(Room r, int[] snapsBefore, RewindKind kind, int by, int to, string code)
        {
            RewindNote first = null;
            for (int s = 0; s < r.Host.SeatCount; s++)
            {
                Assert.AreEqual(snapsBefore[s] + 1, r.Net.Streams[r.Conn[s]].Snapshots, $"좌석 {s}: 되돌림 갱신은 스냅샷 하나");
                var marked = r.Net.All<GameUpdate>(r.Conn[s]).FindAll(u => u.Rewind != null);
                Assert.IsNotEmpty(marked, $"좌석 {s}: 되돌림 표지");
                var n = marked[marked.Count - 1].Rewind;
                Assert.AreEqual((kind, by, to, r.Host.Game.Epoch), (n.Kind, n.By, n.To, n.Epoch), $"좌석 {s}");
                Assert.AreEqual(StateHash.Of(r.Host.Game.ViewFor(s)), StateHash.Of(r.View(s)), $"좌석 {s}: 받은 보기 = 지금 ViewFor");
                Assert.AreEqual(r.Host.Game.Epoch, OfferOf(r, s).Epoch, $"좌석 {s}: 제안의 Epoch");
                var notes = r.Notices(s, code);
                Assert.AreEqual(1, notes.Count, $"좌석 {s}: {code} 알림");
                Assert.AreEqual(by, notes[0].Seat);
                first ??= n;
            }
            return first;
        }

        [Test]
        public void M10_무르기_방_손님의무르기는모두에게스냅샷과되돌림표지()
        {
            var r = new Room(ctx, Cfg(Free), 3);
            int g = ToUndoableGuest(r);
            var offer = OfferOf(r, g);
            Assert.AreEqual((UndoMode.Free, UndoBlock.None, false), (offer.Mode, offer.Block, offer.HostList), "손님은 되돌리기 목록을 열지 못한다");
            Assert.IsTrue(OfferOf(r, 0).HostList);
            int at = r.Host.Game.CommandCount;
            ulong hash = r.Host.Game.Hash;
            r.Now += 10000;
            r.Host.Tick(r.Now);
            var clock = r.Host.Clock.Seats[g];
            Assert.AreEqual(ClockKind.MapTurn, clock.Running);
            long left = clock.LeftMs, bank = clock.BankMs;
            Assert.Less(left, r.Host.Settings.ClockLengths().MapTurnMs, "턴 시간이 흘렀다");
            var snaps = SnapshotsOf(r);

            r.Host.OnMessage(r.Conn[g], new UndoStep { To = offer.StepTo }, r.Now);
            Assert.AreEqual(offer.StepTo, r.Host.Game.CommandCount);
            Assert.AreEqual(1, r.Host.Game.Epoch);
            var note = AllGotRewind(r, snaps, RewindKind.Step, g, offer.StepTo, NetCode.Undone);
            Assert.AreEqual(r.Host.Game.NextSeq, note.NextSeq);
            Assert.AreEqual(RoomPhase.InGame, r.Host.Phase);
            for (int s = 0; s < r.Host.SeatCount; s++) Assert.AreEqual(0, r.Notices(s, NetCode.UndoDenied).Count, $"좌석 {s}");
            // 개인 무르기는 시계를 그대로 둔다(맵 턴 단위가 턴에 붙어 다시 차지 않는다)
            clock = r.Host.Clock.Seats[g];
            Assert.AreEqual((ClockKind.MapTurn, left, bank), (clock.Running, clock.LeftMs, clock.BankMs));

            // 되돌린 뒤 같은 명령 = 같은 결과(방에서도 — 같은 선택 Id · 같은 Rng)
            r.Until(() => r.Host.Game.CommandCount >= at, Walk);
            Assert.AreEqual(at, r.Host.Game.CommandCount);
            Assert.AreEqual(hash, r.Host.Game.Hash, "다시 걸은 판 = 무르기 전 판");
        }

        [Test]
        public void M10_무르기_방_낡은To와다른좌석의요청은거절()
        {
            var r = new Room(ctx, Cfg(Free), 3);
            int g = ToUndoableGuest(r);
            int other = g == 1 ? 2 : 1, at = r.Host.Game.CommandCount;
            var offer = OfferOf(r, g);

            r.Host.OnMessage(r.Conn[other], new UndoStep { To = offer.StepTo }, r.Now); // 남의 턴
            r.Host.OnMessage(r.Conn[0], new UndoStep { To = offer.StepTo }, r.Now);     // 방장도 남의 턴이면 한 수 무르기는 못 한다
            r.Host.OnMessage(r.Conn[g], new UndoStep { To = offer.StepTo - 1 }, r.Now); // 낡은 To
            r.Host.OnMessage(r.Conn[g], new UndoStep { To = at }, r.Now);
            Assert.AreEqual((1, 1, 2), (r.Notices(other, NetCode.UndoDenied).Count, r.Notices(0, NetCode.UndoDenied).Count, r.Notices(g, NetCode.UndoDenied).Count), "거절은 그 좌석에게만");
            foreach (var n in r.Net.All<NoticeMsg>(r.Conn[other])) Assert.AreNotEqual(NetCode.Undone, n.Code);
            Assert.AreEqual((at, 0, 0), (r.Host.Game.CommandCount, r.Host.Game.Epoch, r.Host.Game.Undos), "판은 그대로");

            // 제대로 된 To는 되고, 같은 요청을 한 번 더(두 번 눌림)는 거절
            r.Host.OnMessage(r.Conn[g], new UndoStep { To = offer.StepTo }, r.Now);
            Assert.AreEqual((offer.StepTo, 1), (r.Host.Game.CommandCount, r.Host.Game.Epoch));
            r.Host.OnMessage(r.Conn[g], new UndoStep { To = offer.StepTo }, r.Now);
            Assert.AreEqual(3, r.Notices(g, NetCode.UndoDenied).Count);
            Assert.AreEqual((offer.StepTo, 1), (r.Host.Game.CommandCount, r.Host.Game.Epoch));
        }

        [Test]
        public void M10_무르기_방_일시정지중에는거절()
        {
            var r = new Room(ctx, Cfg(Free), 3);
            int g = ToUndoableGuest(r);
            var offer = OfferOf(r, g);
            int at = r.Host.Game.CommandCount;

            r.Host.OnMessage(r.Conn[0], new SetPaused { On = true }, r.Now);
            Assert.IsTrue(r.Host.Paused);
            Assert.AreEqual(offer.StepTo, r.Host.Game.OfferFor(g).StepTo, "핵은 무를 수 있다고 본다");
            r.Host.OnMessage(r.Conn[g], new RequestResync(), r.Now);
            var paused = OfferOf(r, g);
            Assert.AreEqual((-1, UndoBlock.Paused), (paused.StepTo, paused.Block), "일시정지 중의 제안 = 못 함(까닭 Paused)");
            r.Host.OnMessage(r.Conn[g], new UndoStep { To = offer.StepTo }, r.Now);
            Assert.AreEqual(1, r.Notices(g, NetCode.UndoDenied).Count);
            Assert.AreEqual((at, 0, 0), (r.Host.Game.CommandCount, r.Host.Game.Epoch, r.Host.Game.Undos), "핵까지 가지 않는다");

            // 풀면 지금 제안을 다시 보낸다(다음 행동까지 「일시정지 중」으로 막힌 채 남지 않게) · 같은 요청이 된다
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = false }, r.Now);
            for (int s = 0; s < r.Host.SeatCount; s++)
            {
                var again = OfferOf(r, s);
                Assert.AreNotEqual(UndoBlock.Paused, again.Block, $"좌석 {s}: 풀린 뒤의 제안");
                Assert.AreEqual(r.Host.Game.OfferFor(s).StepTo, again.StepTo, $"좌석 {s}");
            }
            Assert.AreEqual((offer.StepTo, offer.Block), (OfferOf(r, g).StepTo, OfferOf(r, g).Block), "무를 수 있는 좌석은 다시 무를 수 있다");
            r.Host.OnMessage(r.Conn[g], new UndoStep { To = offer.StepTo }, r.Now);
            Assert.AreEqual((offer.StepTo, 1), (r.Host.Game.CommandCount, r.Host.Game.Epoch));

            // 방장의 되돌리기는 일시정지 중에도 된다 [가정 U7] — 정지는 그대로(다시 걸어 지금 턴 시작이 목록에 오게 한다)
            r.Until(() => r.Host.Game.CommandCount >= at, Walk);
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = true }, r.Now);
            r.Host.OnMessage(r.Conn[0], new RequestUndoPoints(), r.Now);
            var points = r.Net.Last<UndoPointsMsg>(r.Conn[0]).Points;
            Assert.IsNotEmpty(points);
            r.Host.OnMessage(r.Conn[0], new RewindTo { To = points[points.Count - 1].To }, r.Now);
            Assert.AreEqual((points[points.Count - 1].To, 2), (r.Host.Game.CommandCount, r.Host.Game.Epoch));
            Assert.IsTrue(r.Host.Paused);
            Assert.AreEqual(1, r.Notices(g, NetCode.Rewound).Count);
        }

        [Test]
        public void M10_무르기_방_방장되돌리기는시계를다시세우고뱅크는그대로()
        {
            var r = new Room(ctx, Cfg(Free), 2);
            int g = ToUndoableGuest(r);
            var len = r.Host.Settings.ClockLengths();
            long bank = r.Host.Clock.Seats[g].BankMs;
            Assert.Greater(bank, 5000);
            // 그 좌석이 턴 시간을 다 쓰고 뱅크 5초를 쓴다
            r.Now += len.MapTurnMs + 5000;
            r.Host.Tick(r.Now);
            var c = r.Host.Clock.Seats[g];
            Assert.AreEqual((ClockKind.MapTurn, 0L, bank - 5000), (c.Running, c.LeftMs, c.BankMs));

            r.Host.OnMessage(r.Conn[0], new RequestUndoPoints(), r.Now);
            var now = r.Net.Last<UndoPointsMsg>(r.Conn[0]).Points.Find(p => p.Current);
            Assert.IsNotNull(now, "지금 턴의 시작 줄");
            Assert.AreEqual(g, now.Seat);
            var snaps = SnapshotsOf(r);
            r.Host.OnMessage(r.Conn[0], new RewindTo { To = now.To }, r.Now);
            Assert.AreEqual(now.To, r.Host.Game.CommandCount);
            AllGotRewind(r, snaps, RewindKind.Host, 0, now.To, NetCode.Rewound);
            // 알림은 되돌린 라운드를 싣는다(알림 띠의 줄마다 제 라운드 — 갱신보다 먼저 닿아도)
            int round = r.Host.Game.HostCopy().Round;
            Assert.Greater(round, 0);
            for (int s = 0; s < r.Host.SeatCount; s++) Assert.AreEqual(round, r.Notices(s, NetCode.Rewound)[0].Round, $"좌석 {s}");
            Assert.IsTrue(MessageCodec.TryDecodeServer(MessageCodec.Encode(r.Notices(g, NetCode.Rewound)[0], 1), out var wire, out string error), error);
            Assert.AreEqual(round, ((NoticeMsg)wire).Round, "전선을 지나도");

            // 시계는 새로(그 턴의 기본 시간이 다시 찬다), 뱅크는 쓴 그대로(돌려주지 않는다) [가정 U8]
            c = r.Host.Clock.Seats[g];
            Assert.AreEqual((ClockKind.MapTurn, (long)len.MapTurnMs, bank - 5000), (c.Running, c.LeftMs, c.BankMs));
            Assert.AreEqual(r.Now + len.MapTurnMs + bank - 5000, r.Host.NextEventMs, "턴 + 남은 뱅크");
            var sent = r.Net.Last<ClockUpdate>(r.Conn[g]).Clock.Seats[g];
            Assert.AreEqual(((long)len.MapTurnMs, bank - 5000), (sent.LeftMs, sent.BankMs), "손님 시계도 새 값");
        }

        [Test]
        public void M10_무르기_방_라운드충전을다시하지않는다()
        {
            var r = new Room(ctx, Cfg(Free), 2);
            var len = r.Host.Settings.ClockLengths();
            long Bank(int s) => r.Host.Clock.Seats[s].BankMs;
            int Round() => r.Host.Game.HostCopy().Round;
            // 2라운드 첫 맵 행동까지 — 그 라운드 충전이 들어갔다
            r.Until(() => Round() >= 2 && Seat(r) >= 0 && r.Mine(Seat(r)).Kind == ChoiceKind.MapAction);
            r.Now = r.Host.AnimEndMs;
            r.Host.Tick(r.Now);
            var charged = new[] { Bank(0), Bank(1) };
            for (int s = 0; s < 2; s++) Assert.AreEqual(len.BankStartMs + len.BankRoundMs, charged[s], $"좌석 {s}: 2라운드 충전");

            r.Host.OnMessage(r.Conn[0], new RequestUndoPoints(), r.Now);
            var back = r.Net.Last<UndoPointsMsg>(r.Conn[0]).Points.Find(p => p.Round == 1);
            Assert.IsNotNull(back, "1라운드 턴 시작 줄");
            r.Host.OnMessage(r.Conn[0], new RewindTo { To = back.To }, r.Now);
            Assert.AreEqual(1, Round());
            for (int s = 0; s < 2; s++) Assert.AreEqual(charged[s], Bank(s), $"좌석 {s}: 되돌려도 뱅크는 그대로");

            // 2라운드를 다시 지나도 충전하지 않고, 3라운드는 그대로 충전한다
            r.Until(() => Round() >= 2);
            r.Host.Tick(r.Now);
            for (int s = 0; s < 2; s++) Assert.AreEqual(charged[s], Bank(s), $"좌석 {s}: 같은 라운드를 다시 충전하지 않는다");
            r.Until(() => Round() >= 3);
            r.Host.Tick(r.Now);
            for (int s = 0; s < 2; s++) Assert.AreEqual(charged[s] + len.BankRoundMs, Bank(s), $"좌석 {s}: 다음 라운드 충전");
        }

        [Test]
        public void M10_무르기_방_결과뒤되돌리면게임중으로()
        {
            var r = new Room(ctx, Cfg(Free), 2);
            var bot = Bot(5);
            r.Until(() => r.Host.Phase == RoomPhase.Result, bot, 60000);
            Assert.IsTrue(r.Host.Game.GameOver);
            int end = r.Host.Game.CommandCount;
            ulong hash = r.Host.Game.Hash;

            r.Host.OnMessage(r.Conn[0], new RequestUndoPoints(), r.Now);
            var points = r.Net.Last<UndoPointsMsg>(r.Conn[0]).Points;
            Assert.IsNotEmpty(points, "결과 화면에서도 목록을 연다");
            var snaps = SnapshotsOf(r);
            r.Host.OnMessage(r.Conn[0], new RewindTo { To = points[0].To }, r.Now);
            Assert.AreEqual(RoomPhase.InGame, r.Host.Phase, "결과 → 게임 중");
            Assert.IsFalse(r.Host.Game.GameOver);
            AllGotRewind(r, snaps, RewindKind.Host, 0, points[0].To, NetCode.Rewound);
            for (int s = 0; s < 2; s++) Assert.AreEqual(RoomPhase.InGame, r.Net.Last<RoomUpdate>(r.Conn[s]).Room.Phase, $"좌석 {s}: 방 상태");

            // 같은 답을 다시 내면 같은 끝(같은 선택 Id · 같은 Rng)
            r.Until(() => r.Host.Phase == RoomPhase.Result, bot, 60000);
            Assert.AreEqual((end, hash), (r.Host.Game.CommandCount, r.Host.Game.Hash));
        }

        [Test]
        public void M10_무르기_방_되돌림전에낸명령은Rewound로거절()
        {
            var r = new Room(ctx, Cfg(Free), 3);
            int g = ToUndoableGuest(r);
            r.Host.OnMessage(r.Conn[g], new UndoStep { To = OfferOf(r, g).StepTo }, r.Now);
            Assert.AreEqual(1, r.Host.Game.Epoch);
            int at = r.Host.Game.CommandCount;
            var c = r.Mine(g);
            Assert.IsNotNull(c);

            // 되돌림 알림 전의 Epoch(0)로 낸 답 = Rewound · 판은 그대로
            r.Host.OnMessage(r.Conn[g], new Submit { Command = Command.AnswerTo(c.Id, c.Default.Clone()), Epoch = 0 }, r.Now);
            var rej = r.Net.Last<RejectedMsg>(r.Conn[g]);
            Assert.IsNotNull(rej);
            Assert.AreEqual((RejectReason.Rewound, c.Id), (rej.Reason, rej.ChoiceId));
            Assert.AreEqual(at, r.Host.Game.CommandCount);
            // 방장 좌석도 같은 길(링크 한 프로세스)
            r.Host.OnMessage(r.Conn[0], new Submit { Command = Command.AnswerTo(c.Id, c.Default.Clone()), Epoch = 0 }, r.Now);
            Assert.AreEqual(RejectReason.Rewound, r.Net.Last<RejectedMsg>(r.Conn[0]).Reason);

            // 받은 Epoch로 내면 들어간다
            r.Host.OnMessage(r.Conn[g], new Submit { Command = Command.AnswerTo(c.Id, c.Default.Clone()), Epoch = r.EpochOf(g) }, r.Now);
            Assert.Greater(r.Host.Game.CommandCount, at);
        }

        /// <summary>받은 메시지를 손으로 넣는 좌석 링크(손님 쪽 SeatClient만 본다 — 보내기는 받지 않는다).</summary>
        sealed class FeedLink : ISeatLink
        {
            Action<ServerMsg> rx;
            public void Connect(int seat, Action<ServerMsg> onReceive) => rx = onReceive;
            public void Disconnect(int seat) => rx = null;
            public SubmitResult ToHost(int seat, ClientMsg msg, long nowMs) => SubmitResult.Rejected(RejectReason.BadCommand);
            public void Feed(ServerMsg m) => rx?.Invoke(m);
        }

        /// <summary>그 갱신들을 차례로 받은 손님(좌석 seat)과, 갱신마다의 <see cref="SeatClient.MissedRewind"/>.</summary>
        SeatClient Replay(int seat, List<bool> missed, params List<GameUpdate>[] runs)
        {
            var link = new FeedLink();
            var client = new SeatClient(link, ctx, seat);
            client.Updated += _ => missed.Add(client.MissedRewind);
            foreach (var run in runs)
                foreach (var u in run) link.Feed(u);
            return client;
        }

        [Test]
        public void M10_무르기_방_끊긴동안의되돌림은재접속스냅샷의Epoch로알아챈다()
        {
            // undo.md [가정 U9]: 끊긴 좌석은 되돌림 갱신을 받지 못한다 — 돌아올 때의 스냅샷에는 표지가 없고 제안의 Epoch만 앞선다. 손님은 그것으로 되돌림을 알아채 화면을 정리한다
            var r = new Room(ctx, Cfg(Free), 3);
            int g = ToUndoableGuest(r);
            int other = g == 1 ? 2 : 1, oldConn = r.Conn[other];
            var offer = OfferOf(r, g);
            r.Drop(other);
            r.Host.OnMessage(r.Conn[g], new UndoStep { To = offer.StepTo }, r.Now);
            Assert.AreEqual(1, r.Host.Game.Epoch);
            int newConn = r.Back(other);
            var back = r.Net.Last<GameUpdate>(newConn);
            Assert.IsNotNull(back, "돌아오면 지금 보기");
            Assert.IsNull(back.Rewind, "재접속 스냅샷에는 되돌림 표지가 없다");
            Assert.AreEqual(1, back.Undo.Epoch);

            var missed = new List<bool>();
            var client = Replay(other, missed, r.Net.All<GameUpdate>(oldConn), r.Net.All<GameUpdate>(newConn));
            Assert.Greater(missed.Count, 2);
            Assert.IsTrue(missed[missed.Count - 1], "재접속 스냅샷 = 모르고 지나간 되돌림");
            Assert.AreEqual(1, missed.FindAll(m => m).Count, "그 앞의 갱신은 아니다");
            Assert.AreEqual(1, client.Epoch);

            // 되돌림 갱신을 받은 좌석은 표지로 안다(모르고 지나간 것이 없다) · 같은 Epoch의 스냅샷(Resync)도 아니다
            var seen = new List<bool>();
            Replay(g, seen, r.Net.All<GameUpdate>(r.Conn[g]));
            CollectionAssert.DoesNotContain(seen, true);
            r.Host.OnMessage(r.Conn[g], new RequestResync(), r.Now);
            seen.Clear();
            Replay(g, seen, r.Net.All<GameUpdate>(r.Conn[g]));
            CollectionAssert.DoesNotContain(seen, true, "같은 Epoch의 지금 보기");
        }

        [Test]
        public void M10_무르기_방_남의비공개정보는제안과되돌리기목록에없다()
        {
            var r = new Room(ctx, Cfg(Free), 3);
            r.Until(() => r.Host.Game.CommandCount >= 500 || r.Host.Phase != RoomPhase.InGame, Bot(11));

            // 제안: 차례가 아닌 좌석은 늘 「못 함 · 내 턴 아님(또는 셋업)」 — 차례 좌석의 운 · 남의 명령 여부가 새지 않는다
            int checkedOffers = 0;
            for (int s = 0; s < 3; s++)
                foreach (var u in r.Net.All<GameUpdate>(r.Conn[s]))
                {
                    if (u.View.ActiveSeat == s) continue;
                    Assert.AreEqual(-1, u.Undo.StepTo, $"좌석 {s}");
                    Assert.That(u.Undo.Block, Is.EqualTo(UndoBlock.NotYourTurn).Or.EqualTo(UndoBlock.Setup), $"좌석 {s}");
                    checkedOffers++;
                }
            Assert.Greater(checkedOffers, 100);

            // 되돌리기 목록의 한 일 = 그 턴 주인이 아닌 좌석이 받은 이벤트에서 나올 수 있는 것만
            r.Host.OnMessage(r.Conn[0], new RequestUndoPoints(), r.Now);
            var points = r.Net.Last<UndoPointsMsg>(r.Conn[0]).Points;
            Assert.Greater(points.Count, 5);
            int deeds = 0;
            foreach (var p in points)
            {
                int viewer = (p.Seat + 1) % 3;
                var seen = TurnEvents(r.Net.All<GameUpdate>(r.Conn[viewer]), p.TurnId);
                Assert.IsNotNull(seen, $"좌석 {viewer}가 턴 {p.TurnId} 시작을 받았다");
                var left = new List<UndoDeed>();
                foreach (var e in seen)
                    if (DeedOf(e) is UndoDeed d) left.Add(d);
                foreach (var d in p.Deeds)
                {
                    if (d.Kind == UndoDeedKind.Battle && d.Def >= 0)
                        Assert.IsTrue(seen.Exists(e => e is SentIn si && si.Side == 1 && si.Species == d.Def), $"턴 {p.TurnId}의 배틀 상대 {d.Def}를 좌석 {viewer}는 보지 못했다");
                    int k = left.FindIndex(x => x.Kind == d.Kind && (x.Def < 0 || x.Def == d.Def) && x.Arg == d.Arg);
                    Assert.GreaterOrEqual(k, 0, $"턴 {p.TurnId}의 한 일 {d.Kind}({d.Def},{d.Arg})를 좌석 {viewer}는 보지 못했다");
                    if (d.Kind != UndoDeedKind.Moved) left.RemoveAt(k);
                    deeds++;
                }
            }
            Assert.Greater(deeds, 5);
        }

        /// <summary>받은 갱신에서 그 턴(TurnStarted.TurnId)의 이벤트(다음 TurnStarted 앞까지). 그 턴 시작을 받지 못했으면 null.</summary>
        static List<GameEvent> TurnEvents(List<GameUpdate> got, int turnId)
        {
            List<GameEvent> list = null;
            foreach (var u in got)
                foreach (var e in u.Events)
                {
                    if (e is TurnStarted t)
                    {
                        if (list != null) return list;
                        if (t.TurnId == turnId) list = new List<GameEvent>();
                        continue;
                    }
                    list?.Add(e);
                }
            return list;
        }

        /// <summary>좌석이 받은 이벤트 하나가 「한 일」이면 그 꼴(Def는 알 수 있을 때만 — 포획 · 배틀 상대는 −1로 둔다).</summary>
        static UndoDeed DeedOf(GameEvent e)
        {
            switch (e)
            {
                case Moved _: return new UndoDeed { Kind = UndoDeedKind.Moved };
                case BattleStarted _: return new UndoDeed { Kind = UndoDeedKind.Battle };
                case CaptureAttempt ca when ca.Success: return new UndoDeed { Kind = UndoDeedKind.Caught };
                case Fled _: return new UndoDeed { Kind = UndoDeedKind.Fled };
                case Evolved ev: return new UndoDeed { Kind = UndoDeedKind.Evolved, Def = ev.To };
                case Crafted cr: return new UndoDeed { Kind = UndoDeedKind.Crafted, Arg = cr.ItemKey };
                case StampGained sg: return new UndoDeed { Kind = UndoDeedKind.Stamp, Arg = sg.StampKey };
                case ShrineOffered so: return new UndoDeed { Kind = UndoDeedKind.Offer, Arg = so.ShrineKey };
                case TradeDone _: return new UndoDeed { Kind = UndoDeedKind.Trade };
                case Flew _: return new UndoDeed { Kind = UndoDeedKind.Fly };
            }
            return null;
        }
    }
}
