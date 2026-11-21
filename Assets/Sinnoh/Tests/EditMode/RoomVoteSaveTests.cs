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
    /// plan M7 「세션 ③」 · 02 §10: 방(HostSession)의 투표(강퇴·자리 비움, 02 §6-2), 자리 확인 대기(§6-3), 차단 컷신 E_HOLD(§5-7), 방 저장·이어하기 대기(§7·§6-3).
    /// 세이브는 시험마다 임시 폴더에 쓰고 지운다(사용자 저장 폴더 금지 — RULE-SAVE).
    /// </summary>
    public partial class RoomTests
    {
        // ── 투표 ──

        [Test]
        public void M7_투표_대상뺀접속좌석전원찬성이면가결_반대하나면부결_부결뒤60초대기_강퇴는토큰금지와0초()
        {
            var r = new Room(ctx, Cfg(), 4);
            r.Now = r.Host.AnimEndMs;
            r.Host.Tick(r.Now);

            r.Host.OnMessage(r.Conn[1], new StartVote { Target = 0, Kind = VoteKind.Kick }, r.Now);
            Assert.IsNull(r.Host.Room.Vote, "방장은 강퇴 대상이 될 수 없다");
            Assert.AreEqual(1, r.Notices(1, NetCode.VoteDenied).Count);

            // 자리 비움 투표: 1이 2를 → 0·3이 찬성해야 한다
            r.Host.OnMessage(r.Conn[1], new StartVote { Target = 2, Kind = VoteKind.Away }, r.Now);
            var v = r.Host.Room.Vote;
            Assert.IsNotNull(v);
            CollectionAssert.AreEqual(new[] { 1 }, v.Yes, "연 좌석은 찬성");
            CollectionAssert.AreEquivalent(new[] { 0, 3 }, v.Waiting);
            Assert.AreEqual(HostSession.VoteMs, v.LeftMs);
            Assert.AreEqual(v.Id, r.Net.Last<RoomUpdate>(r.Conn[3]).Room.Vote.Id, "손님도 투표를 본다");
            r.Host.OnMessage(r.Conn[2], new CastVote { VoteId = v.Id, Yes = false }, r.Now);
            Assert.IsNotNull(r.Host.Room.Vote, "대상은 투표하지 못한다");
            r.Host.OnMessage(r.Conn[3], new StartVote { Target = 1, Kind = VoteKind.Kick }, r.Now);
            Assert.AreEqual(v.Id, r.Host.Room.Vote.Id, "투표는 하나씩");
            r.Host.OnMessage(r.Conn[0], new CastVote { VoteId = v.Id, Yes = true }, r.Now);
            Assert.IsNotNull(r.Host.Room.Vote);
            r.Host.OnMessage(r.Conn[3], new CastVote { VoteId = v.Id, Yes = true }, r.Now);
            Assert.IsNull(r.Host.Room.Vote, "모두 찬성 → 가결");
            Assert.IsTrue(r.Host.Room.Seats[2].Away);
            var passed = r.Notices(0, NetCode.VotePassed);
            Assert.AreEqual(1, passed.Count);
            Assert.AreEqual(2, passed[0].Seat);
            Assert.AreEqual(VoteKind.Away, passed[0].Vote);
            r.Host.OnMessage(r.Conn[2], new Submit { Command = Command.AnswerTo(-5, ChoiceAnswer.Of(AnswerKind.Decline)) }, r.Now);
            Assert.IsFalse(r.Host.Room.Seats[2].Away, "그 좌석이 무엇이든 내면 풀린다");

            // 투표할 좌석이 끊기면 남은 접속 좌석만 센다
            r.Host.OnMessage(r.Conn[0], new StartVote { Target = 1, Kind = VoteKind.Away }, r.Now);
            int away = r.Host.Room.Vote.Id;
            r.Host.OnMessage(r.Conn[2], new CastVote { VoteId = away, Yes = true }, r.Now);
            r.Drop(3);
            Assert.IsNull(r.Host.Room.Vote, "3이 끊겨 0·2의 찬성으로 가결");
            Assert.IsTrue(r.Host.Room.Seats[1].Away);
            r.Back(3);

            // 반대 하나면 부결 → 같은 대상·종류는 60초 동안 못 연다
            r.Host.OnMessage(r.Conn[0], new StartVote { Target = 3, Kind = VoteKind.Kick }, r.Now);
            int kick = r.Host.Room.Vote.Id;
            r.Host.OnMessage(r.Conn[1], new CastVote { VoteId = kick, Yes = false }, r.Now);
            Assert.IsNull(r.Host.Room.Vote);
            Assert.AreEqual(1, r.Notices(2, NetCode.VoteFailed).Count);
            r.Now += 30000;
            r.Host.OnMessage(r.Conn[0], new StartVote { Target = 3, Kind = VoteKind.Kick }, r.Now);
            Assert.IsNull(r.Host.Room.Vote, "부결 뒤 대기");
            r.Now += 30000;
            r.Host.OnMessage(r.Conn[0], new StartVote { Target = 3, Kind = VoteKind.Kick }, r.Now);
            Assert.IsNotNull(r.Host.Room.Vote, "60초 뒤에는 다시 연다");

            // 마감 60초는 일시정지 중에도 흐른다 [가정 M7-18]
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = true }, r.Now);
            Assert.AreEqual(r.Now + HostSession.VoteMs, r.Host.NextEventMs, "일시정지 중 다음 일 = 투표 마감");
            r.Now += HostSession.VoteMs;
            r.Host.Tick(r.Now);
            Assert.IsNull(r.Host.Room.Vote, "마감 → 부결");
            Assert.AreEqual(2, r.Notices(2, NetCode.VoteFailed).Count);
            r.Host.OnMessage(r.Conn[0], new SetPaused { On = false }, r.Now);

            // 강퇴 가결: 연결을 E_KICKED로 끊고 토큰 금지, 좌석은 끝까지 0초 기본값
            int conn2 = r.Conn[2];
            r.Host.OnMessage(r.Conn[1], new StartVote { Target = 2, Kind = VoteKind.Kick }, r.Now);
            int k2 = r.Host.Room.Vote.Id;
            r.Host.OnMessage(r.Conn[0], new CastVote { VoteId = k2, Yes = true }, r.Now);
            r.Host.OnMessage(r.Conn[3], new CastVote { VoteId = k2, Yes = true }, r.Now);
            Assert.AreEqual(NetCode.Kicked, r.Net.Closed[conn2]);
            r.Gone[2] = true;
            Assert.IsTrue(r.Host.Room.Seats[2].Kicked);
            Assert.IsTrue(r.Host.Room.Seats[2].Away);
            Assert.IsFalse(r.Host.Room.Seats[2].Connected);
            Assert.AreEqual(NetCode.Kicked, r.Host.Approve(300, Hi("g2"), r.Now).Code, "토큰 금지");
            int before = r.Notices(0, NetCode.AutoResolved).FindAll(n => n.Seat == 2).Count;
            int cc = r.Host.Game.CommandCount;
            r.Until(() => r.Host.Game.CommandCount > cc + 150 && r.Notices(0, NetCode.AutoResolved).FindAll(n => n.Seat == 2).Count > before);
            Assert.IsTrue(r.Host.Room.Seats[2].Away, "강퇴된 좌석은 돌아오지 않는다");
        }

        // ── 자리 확인 대기 ──

        [Test]
        public void M7_자리확인_모르는토큰은대기_방장이맡기면그좌석토큰이바뀌고_거절하면끊는다()
        {
            var r = new Room(ctx, Cfg(), 3);
            r.Now = r.Host.AnimEndMs;
            r.Host.Tick(r.Now);
            Assert.AreEqual(NetCode.InGame, r.Host.Approve(200, Hi("newpc"), r.Now).Code, "빈 좌석이 없으면 그대로 거절");

            r.Drop(2);
            var web = new Hello { BuildId = Build, ContentHash = Content, Token = "newpc", Nickname = "g2", Platform = Platform.Web };
            var a = r.Host.Approve(201, web, r.Now);
            Assert.IsTrue(a.Ok);
            Assert.AreEqual(-1, a.Seat);
            Assert.AreEqual(NetCode.SeatClaim, a.Code);
            Assert.AreEqual(NetCode.SeatClaim, r.Net.Last<NoticeMsg>(201).Code);
            var claims = r.Host.Room.Claims;
            Assert.AreEqual(1, claims.Count);
            Assert.AreEqual(2, claims[0].Suggested, "닉네임이 같은 끊긴 좌석을 추천");
            Assert.AreEqual(Platform.Web, claims[0].Platform);
            Assert.AreEqual(1, r.Net.Last<RoomUpdate>(r.Conn[0]).Room.Claims.Count, "방장은 대기 목록을 받는다");
            Assert.AreEqual(0, r.Net.Last<RoomUpdate>(r.Conn[1]).Room.Claims.Count, "손님에게는 없다");
            Assert.AreEqual(NetCode.Duplicate, r.Host.Approve(201, web, r.Now).Code);

            r.Host.OnMessage(201, new SetPaused { On = true }, r.Now);
            Assert.IsFalse(r.Host.Paused, "대기 연결의 메시지는 받지 않는다");
            r.Host.OnMessage(r.Conn[1], new AssignSeat { ClaimId = claims[0].Id, Seat = 2 }, r.Now);
            Assert.AreEqual(1, r.Host.Room.Claims.Count, "손님은 맡기지 못한다");
            r.Host.OnMessage(r.Conn[0], new AssignSeat { ClaimId = claims[0].Id, Seat = 1 }, r.Now);
            Assert.AreEqual(1, r.Host.Room.Claims.Count, "접속한 좌석에는 못 맡긴다");

            // 둘째 대기는 거절
            Assert.AreEqual(NetCode.SeatClaim, r.Host.Approve(202, Hi("other"), r.Now).Code);
            var other = r.Host.Room.Claims.Find(c => c.Nickname == "other");
            Assert.AreEqual(-1, other.Suggested);
            r.Host.OnMessage(r.Conn[0], new RefuseClaim { ClaimId = other.Id }, r.Now);
            Assert.AreEqual(NetCode.SeatRefused, r.Net.Closed[202]);
            Assert.AreEqual(1, r.Host.Room.Claims.Count);

            // 맡기기 → 재접속과 같다(Welcome · PLAYER_BACK · 지금 보기)
            r.Host.OnMessage(r.Conn[0], new AssignSeat { ClaimId = claims[0].Id, Seat = 2 }, r.Now);
            Assert.AreEqual(0, r.Host.Room.Claims.Count);
            Assert.AreEqual(2, r.Net.Last<Welcome>(201).YourSeat);
            var snap = r.Net.Last<GameUpdate>(201);
            Assert.AreEqual(2, snap.View.Viewer);
            Assert.AreEqual(StateHash.Of(r.Host.Game.ViewFor(2)), StateHash.Of(snap.View));
            Assert.IsTrue(r.Host.Room.Seats[2].Connected);
            Assert.AreEqual(Platform.Web, r.Host.Room.Seats[2].Platform);
            Assert.AreEqual(1, r.Notices(0, NetCode.PlayerBack).Count);
            r.Conn[2] = 201;
            r.Gone[2] = false;

            Assert.AreEqual(NetCode.InGame, r.Host.Approve(203, Hi("g2"), r.Now).Code, "옛 토큰은 이제 그 좌석의 것이 아니다");
            r.Drop(2);
            var back = r.Host.Approve(204, web, r.Now);
            Assert.AreEqual(2, back.Seat, "새 토큰으로 같은 좌석에 돌아온다");
            r.Conn[2] = 204;
            r.Gone[2] = false;
            r.Drop(1);
            Assert.AreEqual(NetCode.SeatClaim, r.Host.Approve(205, Hi("x"), r.Now).Code);
            r.Host.OnMessage(205, new LeaveRoom(), r.Now);
            Assert.AreEqual(0, r.Host.Room.Claims.Count, "대기 연결이 나가면 목록에서 빠진다");
        }

        // ── 차단 컷신 ──

        [Test]
        public void M7_차단컷신_범위안좌석의답은끝날때까지E_HOLD_끝나면받는다()
        {
            var r = new Room(ctx, Cfg(), 3);
            int seen = 0;
            for (int i = 0; i < 20000 && seen == 0; i++)
            {
                for (int seat = 0; seat < 3 && seen == 0; seat++)
                {
                    var c = r.Mine(seat);
                    long hold = r.Host.HoldLeftMs(seat);
                    if (c == null || hold <= 0) continue;
                    Assert.Greater(r.Net.Last<GameUpdate>(r.Conn[seat]).Clock.HoldLeftMs, 0, "좌석 시계에 차단 컷신 남은 시간이 실린다");
                    Assert.LessOrEqual(r.Now + hold, r.Host.AnimEndMs, "차단 컷신은 방장 멈춤 안이다");
                    int rejected = r.Net.All<RejectedMsg>(r.Conn[seat]).Count;
                    r.Answer(seat, c, c.Default.Clone());
                    var rej = r.Net.All<RejectedMsg>(r.Conn[seat]);
                    Assert.AreEqual(rejected + 1, rej.Count);
                    Assert.AreEqual(RejectReason.Hold, rej[rej.Count - 1].Reason);
                    Assert.IsTrue(r.IsOpen(c.Id), "고른 것은 그대로 열려 있다");
                    r.Now += hold - 1;
                    r.Host.Tick(r.Now);
                    Assert.AreEqual(1, r.Host.HoldLeftMs(seat));
                    r.Now += 1;
                    r.Host.Tick(r.Now);
                    Assert.AreEqual(0, r.Host.HoldLeftMs(seat));
                    r.Host.OnMessage(r.Conn[seat], new Submit { Command = Command.AnswerTo(c.Id, c.Default.Clone()) }, r.Now);
                    Assert.AreEqual(rejected + 1, r.Net.All<RejectedMsg>(r.Conn[seat]).Count, "끝나면 받는다(다시 누름)");
                    seen++;
                }
                if (seen > 0) break;
                if (r.Step(ChallengeGalactic)) continue;
                long t = r.Host.NextEventMs;
                Assert.AreNotEqual(long.MaxValue, t, "방이 멈췄다");
                r.Now = Math.Max(r.Now, t);
                r.Host.Tick(r.Now);
            }
            Assert.AreEqual(1, seen, "차단 컷신 중에 열린 선택을 만나지 못했다");
        }

        // ── 방 저장 · 이어하기 ──

        const int SaveCrashAt = 180, SaveEnd = 320;

        static string TempRoot()
        {
            string d = Path.Combine(Path.GetTempPath(), "sinnoh-room-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        }

        HostConfig SaveCfg(ISaveStore store)
        {
            var c = Cfg(new RoomSettings { Seats = 4, Clock = ClockPreset.Relaxed, Galactic = Control.Ai });
            if (store != null) c.Store = id => store;
            c.SavedAt = () => 1794000000000;
            return c;
        }

        /// <summary>좌석 보기의 Legal에서 선택 Id로 고른다(같은 선택이면 같은 답) — 끊고 이어 한 판도 같은 답을 낸다.</summary>
        Func<GameState, PendingChoice, ChoiceAnswer> SavePick => (view, c) =>
        {
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(view, ctx, c, legal);
            var galactic = legal.Find(a => a.Kind == AnswerKind.Challenge && a.Target == SeatId.Galactic);
            if (galactic != null && c.Id % 2 == 0) return galactic;
            if (c.Kind == ChoiceKind.HiddenSpot) return ChoiceAnswer.Of(AnswerKind.Decline);
            return legal[new Pcg32(11, (ulong)c.Id).Next(legal.Count)];
        };

        /// <summary>로비 4자리에 넷이 들어와 방장이 g9를 내보내고(토큰 금지) 셋이 시작한다.</summary>
        Room SaveRoom(ISaveStore store)
        {
            var r = new Room(ctx, net => new HostSession(ctx, net, SaveCfg(store), 0));
            Assert.IsTrue(r.Host.Approve(0, Hi("owner"), 0).Ok);
            Assert.IsTrue(r.Host.Approve(1, Hi("g1"), 0).Ok);
            Assert.IsTrue(r.Host.Approve(9, Hi("g9"), 0).Ok);
            Assert.IsTrue(r.Host.Approve(2, Hi("g2"), 0).Ok);
            r.Host.OnMessage(0, new KickFromLobby { Seat = 2 }, 0);
            Assert.AreEqual(3, r.Host.SeatOf(2), "g2는 4번째 자리");
            Assert.AreEqual(NetCode.Kicked, r.Net.Closed[9]);
            return r;
        }

        [Test]
        public void M7_세이브_끊지않은판해시는저장이어하기해시와같다_로그끝잘림_스냅샷깨짐_자리맡기기_지금재개()
        {
            // 끊지 않은 판
            var whole = StartSaved(null);
            whole.Until(() => whole.Host.Game.CommandCount >= SaveEnd, SavePick);
            ulong hash = whole.Host.Game.Hash;
            int count = whole.Host.Game.CommandCount;

            string root = TempRoot();
            try
            {
                string dir = Path.Combine(root, "room-m7");
                var crash = StartSaved(new SaveStore(dir));
                // 끊기 조금 전에 1번 좌석이 자기 맵 턴에서 뱅크를 쓴다(답하기 전이라 판은 같다) → 일시정지(정지점 — 머리를 바로 쓴다)
                crash.Until(() => crash.Host.Game.CommandCount >= SaveCrashAt - 12 && crash.Mine(1)?.Kind == ChoiceKind.MapAction, SavePick);
                crash.Now = Math.Max(crash.Now, crash.Host.AnimEndMs);
                crash.Host.Tick(crash.Now);
                var turn = crash.Host.Clock.Seats[1];
                Assert.AreEqual(ClockKind.MapTurn, turn.Running);
                long bankBefore = turn.BankMs;
                crash.Now += turn.LeftMs + 150000;
                crash.Host.Tick(crash.Now);
                Assert.AreEqual(bankBefore - 150000, crash.Host.Clock.Seats[1].BankMs, "기본 시간을 다 쓰고 뱅크를 썼다");
                crash.Host.OnMessage(0, new SetPaused { On = true }, crash.Now);
                crash.Host.OnMessage(0, new SetPaused { On = false }, crash.Now);
                crash.Until(() => crash.Host.Game.CommandCount >= SaveCrashAt, SavePick);
                int crashCount = crash.Host.Game.CommandCount;
                // 방장 PC가 꺼진다(저장은 명령마다 쓰여 있다) — 같은 폴더를 넷으로 나눠 이어 본다
                string plain = Path.Combine(root, "plain"), torn = Path.Combine(root, "torn"), broken = Path.Combine(root, "broken"), early = Path.Combine(root, "early");
                foreach (var d in new[] { plain, torn, broken, early }) CopyDir(dir, d);
                File.AppendAllText(Path.Combine(torn, SaveStore.LogFile), "{\"Seq\":99999,\"Se");
                var snaps = Directory.GetFiles(broken, "snap-*.json");
                Assert.AreEqual(SaveStore.KeepSnapshots, snaps.Length, "스냅샷 둘이 있을 만큼 갔다");
                Array.Sort(snaps, (x, y) => SnapNo(y).CompareTo(SnapNo(x)));
                File.WriteAllText(snaps[0], "{broken");

                var head = SaveStore.Load(plain).Head;
                Assert.IsNotNull(head.Room, "방 저장의 머리에 방 몫");
                CollectionAssert.AreEqual(new[] { "owner", "g1", "g2" }, head.Room.Seats.ConvertAll(s => s.Token));
                CollectionAssert.AreEqual(new[] { "g9" }, head.Room.Banned, "로비에서 내보낸 토큰도 금지로 남는다");
                Assert.AreEqual(ClockPreset.Relaxed, head.Room.Settings.Clock);
                Assert.AreEqual(3, head.Room.Bank.Count);
                Assert.Less(head.Room.Bank[1], head.Room.Bank[0], "1번 좌석이 쓴 뱅크가 머리에 남았다");
                TestContext.WriteLine($"[M7 방 저장] 끊음 {crashCount} · 끝 {count} · 뱅크 {string.Join(",", head.Room.Bank)} (충전 라운드 {head.Room.BankRound}) · 스냅샷 {string.Join(",", Array.ConvertAll(Directory.GetFiles(dir, "snap-*.json"), Path.GetFileName))}");

                // ① 그대로: 모두 같은 토큰으로 돌아오면 저절로 시작
                var a = ResumeRoom(plain, out var rec);
                Assert.AreEqual(RoomPhase.Waiting, a.Host.Phase);
                Assert.AreEqual(3, a.Host.Room.Seats.Count);
                Assert.AreEqual(NetCode.Kicked, a.Host.Approve(50, Hi("g9"), 0).Code, "금지 토큰은 이어하기에도 못 들어온다");
                Assert.IsTrue(a.Host.Approve(0, Hi("owner"), 0).Ok);
                Assert.IsTrue(a.Host.Approve(1, Hi("g1"), 0).Ok);
                Assert.AreEqual(RoomPhase.Waiting, a.Host.Phase, "모두 돌아올 때까지 기다린다");
                Assert.IsTrue(a.Host.Approve(2, Hi("g2"), 0).Ok);
                Assert.AreEqual(RoomPhase.InGame, a.Host.Phase, "모두 돌아오면 시작");
                for (int i = 0; i < 3; i++) Assert.AreEqual(rec.Head.Room.Bank[i], a.Host.Clock.Seats[i].BankMs, $"좌석 {i} 뱅크 되살림(다시 충전하지 않음)");
                for (int i = 0; i < 3; i++) Assert.AreEqual(StateHash.Of(a.Host.Game.ViewFor(i)), StateHash.Of(a.View(i)), $"좌석 {i}이 지금 보기 스냅샷을 받았다");
                Finish(a, hash, count, crashCount, "그대로");

                // ② 로그 마지막 줄 잘림
                var b = ResumeRoom(torn, out var recTorn);
                Assert.IsTrue(recTorn.Warnings.Exists(w => w.Contains("잘려")), string.Join(" / ", recTorn.Warnings));
                JoinAll(b);
                Finish(b, hash, count, crashCount, "로그 끝 잘림");

                // ③ 가장 새 스냅샷 깨짐 + 2번 좌석이 새 토큰(웹 손님)으로 → 자리 확인 대기 → 맡기기
                var c = ResumeRoom(broken, out var recBroken);
                Assert.IsTrue(recBroken.Warnings.Exists(w => w.Contains("깨진 스냅샷")), string.Join(" / ", recBroken.Warnings));
                Assert.IsTrue(c.Host.Approve(0, Hi("owner"), 0).Ok);
                Assert.IsTrue(c.Host.Approve(1, Hi("g1"), 0).Ok);
                var web = new Hello { BuildId = Build, ContentHash = Content, Token = "g2-web", Nickname = "g2", Platform = Platform.Web };
                Assert.AreEqual(NetCode.SeatClaim, c.Host.Approve(2, web, 0).Code);
                var claim = c.Host.Room.Claims[0];
                Assert.AreEqual(2, claim.Suggested);
                c.Host.OnMessage(0, new AssignSeat { ClaimId = claim.Id, Seat = claim.Suggested }, 0);
                Assert.AreEqual(RoomPhase.InGame, c.Host.Phase, "맡겨서 모두 돌아왔다");
                Assert.AreEqual("g2-web", SaveStore.Load(broken).Head.Room.Seats[2].Token, "맡긴 좌석의 새 토큰을 바로 쓴다(정지점)");
                Finish(c, hash, count, crashCount, "스냅샷 깨짐 · 자리 맡기기");

                // ④ [지금 재개]: 2번 좌석 없이 시작 → 끊김 → 곧 돌아온다
                var e = ResumeRoom(early, out _);
                Assert.IsTrue(e.Host.Approve(0, Hi("owner"), 0).Ok);
                Assert.IsTrue(e.Host.Approve(1, Hi("g1"), 0).Ok);
                e.Host.OnMessage(1, new StartGame(), 0);
                Assert.AreEqual(RoomPhase.Waiting, e.Host.Phase, "손님은 재개하지 못한다");
                e.Host.OnMessage(0, new StartGame(), 0);
                Assert.AreEqual(RoomPhase.InGame, e.Host.Phase);
                Assert.IsFalse(e.Host.Room.Seats[2].Connected);
                Assert.IsNull(e.View(2));
                e.Gone[2] = true;
                e.Back(2);
                Assert.AreEqual(StateHash.Of(e.Host.Game.ViewFor(2)), StateHash.Of(e.View(2)));
                Finish(e, hash, count, crashCount, "지금 재개");

                // 솔로 저장은 방으로 열지 않는다
                var solo = SaveStore.Load(plain);
                solo.Head.Room = null;
                Assert.Throws<NotSupportedException>(() => HostSession.Resume(ctx, new Net(), SaveCfg(null), solo, 0));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        static int SnapNo(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            return int.Parse(n.Substring("snap-".Length));
        }

        /// <summary>저장 방(또는 저장 없는 같은 방)을 열고 시작한다 — 끊지 않은 판과 끊은 판이 같은 로비를 거친다.</summary>
        Room StartSaved(ISaveStore store)
        {
            var r = SaveRoom(store);
            r.Host.OnMessage(1, new SetReady { Ready = true }, 0);
            r.Host.OnMessage(2, new SetReady { Ready = true }, 0);
            r.Host.OnMessage(0, new StartGame(), 0);
            Assert.AreEqual(RoomPhase.InGame, r.Host.Phase);
            Assert.AreEqual(3, r.Host.SeatCount);
            return r;
        }

        Room ResumeRoom(string dir, out SaveRecord rec)
        {
            var record = SaveStore.Load(dir);
            rec = record;
            var store = new SaveStore(dir);
            return new Room(ctx, net => HostSession.Resume(ctx, net, SaveCfg(store), record, 0));
        }

        static void JoinAll(Room r)
        {
            Assert.IsTrue(r.Host.Approve(0, Hi("owner"), 0).Ok);
            Assert.IsTrue(r.Host.Approve(1, Hi("g1"), 0).Ok);
            Assert.IsTrue(r.Host.Approve(2, Hi("g2"), 0).Ok);
            Assert.AreEqual(RoomPhase.InGame, r.Host.Phase);
        }

        void Finish(Room r, ulong hash, int count, int crashCount, string what)
        {
            TestContext.WriteLine($"  {what}: 이어 받은 자리 {r.Host.Game.CommandCount}");
            Assert.LessOrEqual(r.Host.Game.CommandCount, crashCount, $"{what}: 마지막 쉬는 때로 돌아갔다");
            r.Until(() => r.Host.Game.CommandCount >= SaveEnd, SavePick);
            Assert.AreEqual(count, r.Host.Game.CommandCount, what);
            Assert.AreEqual(hash, r.Host.Game.Hash, $"{what}: 이어 한 판 = 끊지 않은 판");
        }
    }
}
