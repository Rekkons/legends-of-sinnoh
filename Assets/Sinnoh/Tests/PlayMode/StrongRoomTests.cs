using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// plan M8 완료 조건 3(02 §5-7 · M7-24 「호스트 러너」): 방에서 갤럭시단 AI 세기 「강함」 — 조립 지점과 같은 한 벌(<see cref="StrongAgent.Virtual"/>)을 실제 작업 스레드 러너
    /// (<see cref="AgentWorker"/>, 시계 = Stopwatch — 강한 AI가 자기 마감 1.5초로 생각한다) 위에서 돌린다. Unity Mono · 실제 프레임.
    /// ① 물음은 방장 멈춤(연출 대기열)이 끝난 뒤에 나가고, 답은 그 뒤에 들어간다(답이 낸 갱신의 멈춤 끝 ≥ 물은 때의 멈춤 끝) ·
    /// ② 생각하는 동안 게임 시각이 멈춘다 — 좌석 시계(남은 시간 · 뱅크)와 멈춤 · 차단 남은 시간이 줄지 않는다(바깥 시각은 흐른다) ·
    /// ③ 생각하는 중에 판이 바뀌면(방장이 끊긴 좌석을 자리 비움으로) 낡은 답을 버리고 다시 묻는다 · ④ 관전 좌석에 강한 AI 자막(「수읽기」 사유)이 간다.
    /// 망(NGO)은 쓰지 않는다(링크는 받은 스트림을 되읽는 가짜) — 연결 몫은 M7 NetLoopTests · RoomSyncFlowTests.
    /// </summary>
    public sealed class StrongRoomTests
    {
        const string Build = "test-build";
        const string Content = "test-content";
        static Ctx ctx;

        sealed class Link : IRoomLink
        {
            readonly Dictionary<int, StreamReceiver> rx = new Dictionary<int, StreamReceiver>();
            public readonly List<KeyValuePair<int, GameUpdate>> Updates = new List<KeyValuePair<int, GameUpdate>>();

            public void Send(int conn, ServerMsg msg)
            {
                if (!(msg is GameSnapshot) && !(msg is GameDelta)) return;
                if (!rx.TryGetValue(conn, out var r)) rx[conn] = r = new StreamReceiver();
                var u = r.Receive(msg);
                Assert.IsNotNull(u, $"연결 {conn}: 스트림이 어긋났다({r.LastProblem})");
                Updates.Add(new KeyValuePair<int, GameUpdate>(conn, u));
            }

            public void Close(int conn, string code) { }
        }

        static Hello Hi(string token) => new Hello { BuildId = Build, ContentHash = Content, Token = token, Nickname = token, Platform = Platform.Pc };

        /// <summary>시계 값 한 벌(좌석마다 남은 시간 · 뱅크 · 도는 시계 + 멈춤 · 차단 남은 시간).</summary>
        static string Clocks(HostSession h)
        {
            var c = h.Clock;
            var sb = new System.Text.StringBuilder();
            sb.Append("anim ").Append(c.AnimLeftMs).Append(" hold ").Append(c.HoldLeftMs);
            foreach (var s in c.Seats) sb.Append(" | ").Append(s.Running).Append(' ').Append(s.LeftMs).Append('/').Append(s.BankMs);
            return sb.ToString();
        }

        [UnityTest, NUnit.Framework.Timeout(240000)]
        public IEnumerator M8_방_강한AI생각은연출대기열뒤_생각하는동안사람시계와멈춤이줄지않는다_판이바뀌면낡은답을버린다_관전좌석에자막()
        {
            var watch = Stopwatch.StartNew();
            long skip = 0;
            long Wall() => watch.ElapsedMilliseconds + skip;
            var link = new Link();
            var va = StrongAgent.Virtual();
            using (var worker = new AgentWorker(() => watch.ElapsedMilliseconds))
            {
                var cfg = new HostConfig
                {
                    OwnerToken = "owner", OwnerNickname = "owner", RoomId = "room-m8", BuildId = Build, ContentHash = Content, Seed = 20261118, AiSeed = 77,
                    VirtualAgent = va, Runner = worker,
                    Settings = new RoomSettings { Seats = 3, Clock = ClockPreset.Relaxed, GalacticAi = GalacticAiLevel.Strong },
                };
                var host = new HostSession(LoadCtx(), link, cfg, Wall());
                for (int i = 0; i < 3; i++) Assert.IsTrue(host.Approve(i, Hi(i == 0 ? "owner" : "g" + i), Wall()).Ok);
                for (int i = 1; i < 3; i++) host.OnMessage(i, new SetReady { Ready = true }, Wall());
                host.OnMessage(0, new StartGame(), Wall());
                Assert.AreEqual(RoomPhase.InGame, host.Phase);
                Assert.AreEqual(GalacticAiLevel.Strong, va.GalacticLevel, "방 설정 「강함」");

                var sent = new Dictionary<int, long>();
                var askedChoices = new HashSet<int>();
                bool[] gone = new bool[3];
                bool sawBattle = false, staleDone = false;
                int thinks = 0, galacticThinks = 0, serial = 0, staleBefore = -1, maxThinkMs = 0, frames = 0, idle = 0, lastCount = -1;
                long thinkWall = 0;
                string thinkClocks = null;
                var askedEnds = new List<KeyValuePair<long, int>>();   // (물은 때의 멈춤 끝 — 게임 시각, 그때까지 받은 갱신 수)
                float end = UnityEngine.Time.realtimeSinceStartup + 200;
                while (true)
                {
                    Assert.Less(UnityEngine.Time.realtimeSinceStartup, end, $"시간 초과(명령 {host.Game.CommandCount} · 배틀 봄 {sawBattle} · 생각 {thinks})");
                    frames++;
                    host.Tick(Wall());
                    var hostView = host.Game.HostCopy();
                    if (hostView.Battle != null && hostView.Battle.Kind == BattleKind.Galactic) sawBattle = true;
                    if (sawBattle && hostView.Battle == null) break;

                    if (host.AwaitingAi)
                    {
                        if (host.Game.AskSerial != serial)
                        {
                            serial = host.Game.AskSerial;
                            thinks++;
                            thinkWall = watch.ElapsedMilliseconds;
                            var open = hostView.Pending.Find(c => c.Seat == SeatId.Galactic && !c.Answered);
                            if (open != null && hostView.Battle != null && askedChoices.Add(open.Id))
                            {
                                // ① 새 물음(그 선택을 처음 묻는다): 방장 멈춤(연출 대기열)은 이미 끝났다 — 생각은 그 뒤, 답도 그 뒤에 든다
                                Assert.AreEqual(0, host.Clock.AnimLeftMs, "AI에게 묻는 때 = 앞 연출이 끝난 뒤");
                                galacticThinks++;
                                askedEnds.Add(new KeyValuePair<long, int>(host.Game.AnimEndMs, link.Updates.Count));
                                // ③ 강한 AI가 실제로 생각하는(답이 둘 이상) 첫 갤럭시단 물음 중에 판을 바꾼다: 좌석 2가 끊기고 방장이 자리 비움으로(HostOp 줄) — 그 답은 낡아 버리고 다시 묻는다
                                var legal = new List<ChoiceAnswer>();
                                var ov = host.Game.ViewFor(SeatId.Observer);
                                Engine.Legal(ov, ctx, ov.Pending.Find(c => c.Id == open.Id), legal);
                                if (!staleDone && legal.Count >= 2)
                                {
                                    staleDone = true;
                                    staleBefore = host.Game.StaleAiAnswers;
                                    gone[2] = true;
                                    host.OnDisconnected(2, Wall());
                                    host.OnMessage(0, new MarkAway { Seat = 2 }, Wall());
                                }
                            }
                            thinkClocks = Clocks(host);
                        }
                        else
                        {
                            // ② 생각하는 동안 게임 시각이 멈춘다 — 시계 · 멈춤 · 차단이 그대로(바깥 시각만 흐른다)
                            Assert.AreEqual(thinkClocks, Clocks(host), $"생각 {thinks}번째 {watch.ElapsedMilliseconds - thinkWall} ms: 시계가 흘렀다");
                            maxThinkMs = Math.Max(maxThinkMs, (int)(watch.ElapsedMilliseconds - thinkWall));
                        }
                        yield return null;
                        continue;
                    }

                    // 사람 좌석: 내 첫 선택에 답한다(좌석 0은 [도전] 갤럭시단, 그 밖은 기본값) — 차단 중이거나 낸 지 얼마 안 됐으면 기다린다
                    bool acted = false;
                    for (int seat = 0; seat < 3; seat++)
                    {
                        if (gone[seat] || host.HoldLeftMs(seat) > 0) continue;
                        var v = host.Game.ViewFor(seat);
                        var c = v.Pending.Find(x => x.Seat == seat && !x.Answered);
                        if (c == null || (sent.TryGetValue(c.Id, out long at) && Wall() - at < 500)) continue;
                        var a = c.Kind == ChoiceKind.MapAction && seat == 0 ? ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic) : null;
                        if (a != null)
                        {
                            var legal = new List<ChoiceAnswer>();
                            Engine.Legal(v, ctx, c, legal);
                            if (!ChoiceRules.Contains(legal, a)) a = null;
                        }
                        sent[c.Id] = Wall();
                        host.OnMessage(seat, new Submit { Command = Command.AnswerTo(c.Id, a ?? c.Default.Clone()) }, Wall());
                        acted = true;
                    }
                    // 실시각 연출은 기다리지 않는다: 멈춤 끝 · 차단 끝까지 바깥 시각을 건너뛰고, 할 일이 없는 채 세 번이면 다음 정해진 일로(생각 시간은 건너뛰지 않는다 — 위에서 실시각으로 흐른다)
                    if (!host.AwaitingAi)
                    {
                        long hold = 0;
                        for (int s = 0; s < 3; s++) hold = Math.Max(hold, host.HoldLeftMs(s));
                        long next = Math.Max(Wall() + 50 + hold, host.AnimEndMs);
                        int n = host.Game.CommandCount;
                        idle = acted || n != lastCount ? 0 : idle + 1;
                        lastCount = n;
                        if (idle >= 3)
                        {
                            long due = host.NextEventMs;
                            if (due != long.MaxValue && due > next) next = due;
                            idle = 0;
                        }
                        skip += next - Wall();
                    }
                    yield return null;
                }

                // ① 답은 물은 때의 멈춤 끝 뒤에 든다: 물은 뒤 관전 좌석(1)이 처음 받은 갤럭시단 AI 답 갱신의 멈춤 끝(게임 시각) ≥ 물은 때의 멈춤 끝
                int read = 0, spectatorRead = 0, behind = 0;
                foreach (var kv in askedEnds)
                {
                    for (int i = kv.Value; i < link.Updates.Count; i++)
                    {
                        if (link.Updates[i].Key != 1) continue;
                        var u = link.Updates[i].Value;
                        bool aiAnswer = u.Events != null && u.Events.Exists(e => e is ChoiceAnswered ca && ca.ByAi && ca.Seat == SeatId.Galactic);
                        if (!aiAnswer) continue;
                        Assert.GreaterOrEqual(u.AnimEndMs, kv.Key, "AI 답의 갱신 = 앞 연출 뒤에 붙는다");
                        behind++;
                        break;
                    }
                }
                foreach (var kv in link.Updates)
                {
                    if (kv.Value.Events == null) continue;
                    foreach (var e in kv.Value.Events)
                        if (e is ChoiceAnswered ca && ca.ByAi && ca.Seat == SeatId.Galactic && ca.Reason != null && ca.Reason.Rule == AiRule.Lookahead)
                        {
                            read++;
                            if (kv.Key == 1) spectatorRead++;
                            Assert.That(ca.Reason.Power, Is.InRange(0, 100));
                        }
                }
                string line = $"[M8 조건 3] 강한 AI 방 판: 프레임 {frames} · 물음 {thinks}(갤럭시단 배틀 첫 물음 {galacticThinks} · 답이 연출 뒤 {behind}) · 러너 셈 {worker.Decided} · 가장 긴 생각 {maxThinkMs} ms · 낡은 답 {host.Game.StaleAiAnswers - staleBefore} · 늦은 답 {host.Game.LateAi} · 「수읽기」 자막 {read}(관전 좌석 {spectatorRead}) · 명령 {host.Game.CommandCount}";
                Debug.Log(line);
                TestContext.Out.WriteLine(line);
                Assert.IsTrue(sawBattle, "갤럭시단 배틀");
                Assert.Greater(galacticThinks, 0, "갤럭시단 배틀 선택을 러너에 물었다");
                Assert.AreEqual(galacticThinks, behind, "물은 갤럭시단 선택마다 관전 좌석이 그 답을 받았다");
                Assert.Greater(maxThinkMs, 0, "생각에 실제 시간이 들었다(바깥 시각이 흘렀다)");
                Assert.IsTrue(staleDone);
                Assert.GreaterOrEqual(host.Game.StaleAiAnswers - staleBefore, 1, "생각하는 중에 판이 바뀌어 낡은 답을 버렸다");
                Assert.AreEqual(0, host.Game.LateAi, "늦은 답 없음(결정당 1.5초 < 한도 3초)");
                Assert.Greater(spectatorRead, 0, "관전 좌석에 강한 AI 자막");
                host.Close(Wall());
            }
        }

        static Ctx LoadCtx()
        {
            if (ctx != null) return ctx;
            const string dir = "Assets/Sinnoh/Data/Json/";
            Stream F(string f) => File.OpenRead(dir + f);
            using (var b = F("board.json"))
            using (var p = F("pokemon.json"))
            using (var m = F("moves.json"))
            using (var t = F("trainer_cards.json"))
            using (var i = F("items.json"))
            using (var d = F("decks.json"))
            using (var c = F("cards.json"))
            using (var x = F("fixtures.json"))
            using (var e = F("effects.json"))
            {
                var board = BoardLoader.Load(b);
                var db = CardDbLoader.Load(p, m, t);
                var items = ItemDbLoader.Load(i);
                ctx = new Ctx(board, db, DeckDbLoader.Load(d, c, db), FixtureDbLoader.Load(x, board, db, items), items, EffectDbLoader.Load(e, db));
            }
            return ctx;
        }
    }
}
