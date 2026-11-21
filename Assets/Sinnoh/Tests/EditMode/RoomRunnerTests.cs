using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M7 「호스트 러너 · AI 작업 스레드」 · 02 §1-2·§6-3·§7: AI 답은 러너(작업 스레드 <see cref="AgentWorker"/>)가 셈하고 방이 메인 스레드 한 곳에서 넣는다 —
    /// 러너가 있든 없든 같은 판 · 여러 번 돌려 같은 결과(저장 로그 바이트까지) · 방 닫기의 마지막 쓰기와 E_HOST_CLOSED · 낡은 답 버림(가짜 러너).
    /// </summary>
    public partial class RoomTests
    {
        [Test]
        public void M7_AI러너_러너가있든없든같은판_두번돌려로그바이트같음_방닫기는마지막쓰기와E_HOST_CLOSED()
        {
            const int Steps = 2500;
            var plain = Bots(3, Steps);
            string root = TempRoot();
            try
            {
                var logs = new List<string>();
                for (int run = 0; run < 2; run++)
                {
                    string dir = Path.Combine(root, "run" + run);
                    var made = new List<string>();
                    using (var worker = new AgentWorker())
                    {
                        var r = BotsRoom(3, Steps, worker, id =>
                        {
                            made.Add(id);
                            return new SaveWriter(Path.Combine(dir, id));
                        });
                        Assert.AreEqual(plain, Outcome(r), $"{run}번째: 러너가 있든 없든 같은 판(해시 · 명령 수 · 시간 초과 처리 수)");
                        Assert.Greater(worker.Decided, 50, "AI 답을 러너가 셈했다");
                        Assert.AreEqual(0, r.Host.Game.LateAi, "늦은 답 없음");
                        TestContext.WriteLine($"[M7 러너] {run}번째: 명령 {r.Host.Game.CommandCount} · 러너 셈 {worker.Decided} · 낡은 답 {r.Host.Game.StaleAiAnswers}");

                        // 방 닫기(02 §6-3): 마지막 쓰기 → 판의 저장(쓰기 스레드)을 닫는다 → 모든 연결에 E_HOST_CLOSED
                        ulong hash = r.Host.Game.Hash;
                        int count = r.Host.Game.CommandCount;
                        var bank = r.Host.Clock.Seats.ConvertAll(x => x.BankMs);
                        r.Host.Close(r.Now);
                        Assert.AreEqual(RoomPhase.Closed, r.Host.Phase);
                        for (int s = 0; s < 6; s++)
                        {
                            if (r.Gone[s]) continue;
                            Assert.AreEqual(NetCode.HostClosed, r.Net.Closed[r.Conn[s]], $"좌석 {s} 연결을 E_HOST_CLOSED로 끊었다");
                            Assert.AreEqual(1, r.Notices(s, NetCode.HostClosed).Count, $"좌석 {s}에 E_HOST_CLOSED 알림");
                        }
                        Assert.AreEqual(NetCode.HostClosed, r.Host.Approve(999, Hi("late"), r.Now).Code, "닫은 방은 받지 않는다");
                        r.Host.Tick(r.Now + 60000);
                        Assert.AreEqual(count, r.Host.Game.CommandCount, "닫은 방은 더 진행하지 않는다");

                        CollectionAssert.AreEqual(new[] { "room-m7" }, made, "첫 판의 SaveId = RoomId");
                        string save = Path.Combine(dir, "room-m7");
                        var lines = SaveStore.ReadLog(save);
                        Assert.AreEqual(count, lines.Count, "로그는 마지막 명령까지(쓰기 스레드가 비워졌다)");
                        for (int i = 0; i < lines.Count; i++) Assert.AreEqual(i, lines[i].Seq);
                        var rec = SaveStore.Load(save);
                        CollectionAssert.AreEqual(bank, rec.Head.Room.Bank, "머리 = 닫을 때의 뱅크(마지막 쓰기)");
                        Assert.AreEqual(hash, StateHash.Of(LocalSession.Replay(ctx, rec.Head, lines, rec.Snapshot.Checkpoints)), "저장을 다시 적용한 판 = 닫을 때의 판");
                        logs.Add(File.ReadAllText(Path.Combine(save, SaveStore.LogFile)));
                    }
                }
                Assert.AreEqual(logs[0], logs[1], "두 번 돌려 로그 바이트가 같다(스레드 경합 없음)");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>시험이 손으로 답을 돌려주는 러너: 물음을 모으고, 넣어 둔 답을 차례로 내준다.</summary>
        sealed class FakeRunner : IAgentRunner
        {
            public readonly List<AgentRequest> Asked = new List<AgentRequest>();
            public readonly Queue<AgentReply> Ready = new Queue<AgentReply>();

            public void Post(AgentRequest request) => Asked.Add(request);

            public bool TryTake(out AgentReply reply)
            {
                reply = Ready.Count > 0 ? Ready.Dequeue() : null;
                return reply != null;
            }

            public AgentRequest Last => Asked[Asked.Count - 1];

            /// <summary>그 물음에 진짜 답(그 AI가 셈한 것)을 — serial·id를 바꿔 낡은 답을 만들 수 있다.</summary>
            public void Answer(AgentRequest q, int serial = 0, int id = 0) =>
                Ready.Enqueue(new AgentReply { Serial = serial != 0 ? serial : q.Serial, ChoiceId = id != 0 ? id : q.ChoiceId, Decision = q.Agent.Decide(q.Input, q.Budget) });
        }

        /// <summary>러너에 물음이 나갈 때까지 판을 진행한다(물음이 나가면 시각을 더 옮기지 않는다 — 옮기면 기다림 한도가 지난다).</summary>
        static void UntilAsked(Room r)
        {
            for (int i = 0; !r.Host.AwaitingAi; i++)
            {
                Assert.Less(i, 20000, "러너에 물음이 나가지 않았다");
                if (r.Step(ChallengeGalactic) || r.Host.AwaitingAi) continue;
                long t = r.Host.NextEventMs;
                Assert.AreNotEqual(long.MaxValue, t, "방이 멈췄다");
                r.Now = Math.Max(r.Now, t);
                r.Host.Tick(r.Now);
            }
        }

        [Test]
        public void M7_AI러너_낡은답버림_다른Id와번호_늦은답은기본값뒤에버림_물은뒤판이바뀌면다시물음()
        {
            var fake = new FakeRunner();
            var cfg = Cfg(new RoomSettings { Seats = 3, Clock = ClockPreset.Relaxed });
            cfg.Runner = fake;
            var r = new Room(ctx, cfg, 3);
            UntilAsked(r);
            var q = fake.Last;
            Assert.IsTrue(r.IsOpen(q.ChoiceId));
            Assert.AreEqual(SeatId.Observer, q.Input.View.Viewer, "가상 응답자는 관전자 보기로 묻는다");
            Assert.AreEqual(q.ChoiceId, q.Input.Choice.Id);
            Assert.Greater(q.ThinkMs, 0, "마감 = 열린 시각 + 2초까지 남은 시간");
            Assert.LessOrEqual(q.ThinkMs, 2000);
            Assert.AreEqual(r.Now + HostSession.AiGiveUpMs, r.Host.NextEventMs, "답을 기다리는 동안 다음 일 = 기다림 한도");
            int count = r.Host.Game.CommandCount;

            // ① 다른 Id · 다른 번호의 답은 버린다(선택은 그대로 열려 있다)
            fake.Answer(q, id: q.ChoiceId + 1000);
            fake.Answer(q, serial: q.Serial + 7);
            r.Host.Tick(r.Now + 500);
            Assert.IsTrue(r.Host.AwaitingAi);
            Assert.IsTrue(r.IsOpen(q.ChoiceId));
            Assert.AreEqual(count, r.Host.Game.CommandCount);
            Assert.AreEqual(2, r.Host.Game.StaleAiAnswers);

            // ② 늦은 답: 한도(바깥 시각)를 넘기면 기본값(Timeout)으로 닫고, 그 뒤 온 진짜 답은 버린다
            r.Now += HostSession.AiGiveUpMs;
            r.Host.Tick(r.Now);
            Assert.IsFalse(r.IsOpen(q.ChoiceId), "기본값으로 닫혔다");
            Assert.AreEqual(1, r.Host.Game.LateAi);
            Assert.IsTrue(new List<string>(r.Host.Game.Warnings).Exists(w => w.Contains("늦어")), string.Join(" / ", r.Host.Game.Warnings));
            Assert.IsFalse(AnsweredByAi(r, q.ChoiceId, 0), "AI 답이 아니라 시간 초과 기본값");
            int afterLate = r.Host.Game.CommandCount;
            ulong hashLate = r.Host.Game.Hash;
            fake.Answer(q);
            r.Host.Tick(r.Now);
            Assert.AreEqual(afterLate, r.Host.Game.CommandCount, "늦게 온 답은 넣지 않는다");
            Assert.AreEqual(hashLate, r.Host.Game.Hash);

            // ③ 물은 뒤 판이 바뀌면(방장이 끊긴 좌석을 자리 비움으로 — HostOp 줄) 답을 버리고 다시 묻는다 → 새 물음의 답은 넣는다
            UntilAsked(r);
            Assert.AreEqual(3, r.Host.Game.StaleAiAnswers, "늦게 온 답은 다음 물음 전에 버렸다");
            var q2 = fake.Last;
            Assert.AreNotEqual(q.Serial, q2.Serial);
            r.Drop(2);
            r.Host.OnMessage(r.Conn[0], new MarkAway { Seat = 2 }, r.Now);
            fake.Answer(q2);
            r.Host.Tick(r.Now);
            Assert.IsTrue(r.Host.AwaitingAi, "다시 물었다");
            var q3 = fake.Last;
            Assert.AreEqual(q2.ChoiceId, q3.ChoiceId, "같은 선택을 다시 묻는다");
            Assert.AreNotEqual(q2.Serial, q3.Serial);
            Assert.AreEqual(4, r.Host.Game.StaleAiAnswers);
            fake.Answer(q3);
            r.Host.Tick(r.Now);
            Assert.IsFalse(r.IsOpen(q3.ChoiceId), "맞는 답은 넣는다");
            Assert.IsTrue(AnsweredByAi(r, q3.ChoiceId, 0), "AI 답(사유 표시)");
        }

        [Test]
        public void M8_원칙_사람갤럭시단이답하지않으면강한AI가넘겨받은때부터2초로답한다()
        {
            // 결정 30 ③: 갤럭시단을 맡은 사람이 시간 안에 답하지 않으면 강한 AI가 대신 답한다 — 물음에 실린 AI = VirtualAgents.Strong, 마감 = 넘겨받은 때 + 2초
            // (고치기 전에는 마감을 선택이 열린 때부터 세어 시계 끝에는 이미 지나 ThinkMs 0 — 강한 AI가 한 번도 못 굴리고 원칙 답을 냈다). 방장이 조종을 AI로 바꾸면 다음 물음은 세기 줄(원칙)
            var fake = new FakeRunner();
            var principle = new PrincipleAgent();
            var strong = new PrincipleAgent(); // 표지(물음에 실린 AI만 본다)
            var va = new VirtualAgents { Principle = principle, Strong = strong };
            var cfg = Cfg(new RoomSettings { Seats = 3, Galactic = Control.Human });
            cfg.VirtualAgent = va;
            cfg.Runner = fake;
            var r = new Room(ctx, cfg, 3);
            Assert.IsTrue(va.GalacticHuman, "방 설정 「사람」을 맞췄다");
            AgentRequest handed = null;
            int other = 0;
            for (int i = 0; i < 40000 && handed == null; i++)
            {
                if (r.Host.AwaitingAi)
                {
                    var q = fake.Last;
                    var c = r.Host.Game.HostCopy().Pending.Find(x => x.Id == q.ChoiceId);
                    if (c != null && c.Seat == SeatId.Galactic)
                    {
                        handed = q;
                        break;
                    }
                    Assert.AreSame(principle, q.Agent, "야생 · 그 밖의 가상 응답자 = 원칙");
                    fake.Answer(q);
                    r.Host.Tick(r.Now);
                    other++;
                    continue;
                }
                if (r.Step(ChallengeGalactic)) continue;
                long t = r.Host.NextEventMs;
                Assert.AreNotEqual(long.MaxValue, t, "방이 멈췄다");
                r.Now = Math.Max(r.Now, t);
                r.Host.Tick(r.Now);
            }
            Assert.IsNotNull(handed, "사람 조종 갤럭시단 선택이 시계 끝에 AI에게 넘어갔다");
            Assert.AreSame(strong, handed.Agent, "대신 답하는 AI = 강한 AI(결정 30 ③)");
            Assert.AreEqual(new AiSettings().DeadlineMs, handed.ThinkMs, "마감 = 넘겨받은 때 + 2초");
            Assert.Greater(r.Notices(0, NetCode.AutoResolved).Count, 0, "시간 초과 알림");
            fake.Answer(handed);
            r.Host.Tick(r.Now);
            Assert.IsFalse(r.IsOpen(handed.ChoiceId), "강한 AI 답을 넣었다");

            // 방장이 게임 중 조종을 AI로(세기 원칙 그대로) → 다음 갤럭시단 물음은 원칙 AI · 마감은 열린 때부터
            r.Host.OnMessage(r.Conn[0], new ChangeSettings { Settings = new RoomSettings { Seats = 3, Galactic = Control.Ai } }, r.Now);
            Assert.IsFalse(va.GalacticHuman);
            AgentRequest next = null;
            for (int i = 0; i < 40000 && next == null; i++)
            {
                if (r.Host.AwaitingAi)
                {
                    var q = fake.Last;
                    var c = r.Host.Game.HostCopy().Pending.Find(x => x.Id == q.ChoiceId);
                    if (c != null && c.Seat == SeatId.Galactic) next = q;
                    else
                    {
                        fake.Answer(q);
                        r.Host.Tick(r.Now);
                    }
                    continue;
                }
                if (r.Step(ChallengeGalactic)) continue;
                long t = r.Host.NextEventMs;
                Assert.AreNotEqual(long.MaxValue, t, "방이 멈췄다");
                r.Now = Math.Max(r.Now, t);
                r.Host.Tick(r.Now);
            }
            Assert.IsNotNull(next, "AI 조종 갤럭시단 물음");
            Assert.AreSame(principle, next.Agent, "AI 조종 · 세기 원칙 = 원칙 AI");
            Assert.Greater(next.ThinkMs, 0);
            Assert.LessOrEqual(next.ThinkMs, new AiSettings().DeadlineMs);
            TestContext.WriteLine($"[M8 원칙 AI 점검] 넘겨받은 물음 ThinkMs {handed.ThinkMs} · 그 밖의 AI 물음 {other} · AI 조종 물음 ThinkMs {next.ThinkMs}");
        }
    }
}
