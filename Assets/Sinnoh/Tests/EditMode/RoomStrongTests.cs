using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using Sinnoh.Rules.Tutorial;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M8 「연결」(01 §9-6 · 결정 26 · 30 ③): 조립 지점이 꽂는 가상 응답자 AI 한 벌(<see cref="StrongAgent.Virtual"/>)과 세기 — 솔로 설정 · 세이브 머리 · 방 설정에 따라
    /// 갤럭시단 선택에 강한 AI가 답한다(자막 「수읽기」), 이어하기는 머리의 세기 그대로, 튜토리얼은 원칙. 강한 AI는 굴리기 한도를 줄인 것(시험 시간 — 고르기만 본다).
    /// 방에서 AI 생각이 연출 뒤에 숨고 사람 시계가 멈추는 것(완료 조건 3)은 PlayMode <c>StrongRoomTests</c>.
    /// </summary>
    public partial class RoomTests
    {
        /// <summary>시험용 강한 AI: 「강함」 꼴(Tree · 깊이 8 · 명단 거르기)에 굴리기 한도만 줄였다. 답마다 원칙 답과 자막을 적는다.</summary>
        sealed class ToldAgent : IAgent
        {
            public readonly StrongAgent Inner = new StrongAgent { Search = SearchKind.Tree, Depth = StrongAgent.StrongDepth, ScreenLineup = true, MaxSimulations = 48 };
            public readonly List<KeyValuePair<AgentDecision, bool>> Told = new List<KeyValuePair<AgentDecision, bool>>();

            public AgentDecision Decide(AgentInput input, AgentBudget budget)
            {
                var d = Inner.Decide(input, budget);
                var legal = new List<ChoiceAnswer>();
                Engine.Legal(input.View, input.Ctx, input.Choice, legal);
                Told.Add(new KeyValuePair<AgentDecision, bool>(d, d.Answer.SameAs(PrincipleAgent.Choose(input.View, input.Ctx, input.Choice, legal).Answer)));
                return d;
            }
        }

        static void AssertTold(ToldAgent strong, string at)
        {
            int read = 0;
            foreach (var kv in strong.Told)
            {
                var r = kv.Key.Reason;
                if (r.Rule != AiRule.Lookahead) continue;
                read++;
                Assert.That(r.Power, Is.InRange(0, 100), at + ": 승산 %");
                Assert.GreaterOrEqual(r.Bonus, 0, at);
                if (kv.Value) Assert.AreEqual(0, r.Bonus, at + ": 원칙 답과 같으면 「기본 수보다」가 없다");
            }
            Assert.Greater(read, 0, at + ": 강한 AI가 갤럭시단 배틀에서 「수읽기」로 답했다");
        }

        [Test]
        public void M8_방_솔로는세기에따라강한AI가갤럭시단에답한다_자막은수읽기_원칙세기는원칙답()
        {
            // 조립 지점의 한 벌 = 원칙 + 「강함」(StrongAgent.Strong — 결정당 마감 1.5초 · 한도 1024)
            var made = StrongAgent.Virtual();
            Assert.IsInstanceOf<PrincipleAgent>(made.Principle);
            var big = made.Strong as StrongAgent;
            Assert.IsNotNull(big);
            Assert.AreEqual((SearchKind.Tree, StrongAgent.StrongDepth, StrongAgent.StrongSimulations, StrongAgent.StrongThinkMs, true),
                (big.Search, big.Depth, big.MaxSimulations, big.ThinkMs, big.ScreenLineup), "「강함」 조립");
            Assert.AreEqual(GalacticAiLevel.Principle, made.GalacticLevel, "세기는 세션이 맞춘다(기본 원칙)");
            Assert.IsNull(new RoomSettings { GalacticAi = GalacticAiLevel.Strong }.Problem(1), "방 설정 「강함」은 틀린 값이 아니다");
            Assert.AreEqual("galactic_ai", new RoomSettings { GalacticAi = (GalacticAiLevel)99 }.Problem(1));

            foreach (var level in new[] { GalacticAiLevel.Strong, GalacticAiLevel.Principle })
            {
                // 조무래기 배틀의 첫 선택이 열린 판(시험대 장면)을 솔로 설정으로 연다 — 내 답은 첫 합법 답
                var scene = GalacticBench.Scene(ctx, BenchScene.Grunt, 3);
                var strong = new ToldAgent();
                var va = new VirtualAgents { Principle = new PrincipleAgent(), Strong = strong };
                var s = SoloGame.Start(ctx, new SoloSettings { GalacticAi = level }, 1, 2, va, 0, prepare: st => st.CopyFrom(scene));
                Assert.AreEqual(level, va.GalacticLevel, "솔로 세기를 맞췄다");
                Assert.IsFalse(va.GalacticHuman);
                var me = new SeatClient(s.Link, ctx, 0);
                var said = new List<AiReason>();
                me.Updated += u =>
                {
                    foreach (var e in u.Events)
                        if (e is ChoiceAnswered a && a.ByAi && a.Seat == SeatId.Galactic) said.Add(a.Reason);
                };
                long t = 0;
                for (int i = 0; i < 3000 && s.ViewFor(0).Battle != null; i++)
                {
                    var c = me.FirstChoice();
                    if (c != null) me.Answer(c, me.Legal(c)[0], t);
                    t = Math.Max(t + 100, s.AnimEndMs);
                    s.Tick(t);
                }
                Assert.IsNull(s.ViewFor(0).Battle, level + ": 배틀이 끝났다");
                Assert.Greater(said.Count, 0, level + ": 갤럭시단 AI 답");
                if (level == GalacticAiLevel.Strong)
                {
                    AssertTold(strong, "강함");
                    Assert.IsTrue(said.Exists(r => r != null && r.Rule == AiRule.Lookahead), "자막(수읽기)이 내 보기의 이벤트에 실린다");
                }
                else
                {
                    Assert.AreEqual(0, strong.Told.Count, "원칙 세기는 강한 AI에게 묻지 않는다");
                    Assert.IsFalse(said.Exists(r => r != null && r.Rule == AiRule.Lookahead), "원칙 AI 답은 지금 그대로(수읽기 없음)");
                }
            }
        }

        [Test]
        public void M8_방_이어하기는머리의세기그대로_솔로와방_튜토리얼은원칙()
        {
            string root = TempRoot();
            try
            {
                // 솔로: 셋업을 첫 합법 답으로 끝내 스냅샷을 남기고 → 다른 세기로 맞춰 둔 새 한 벌로 이어 받는다 → 머리의 세기
                foreach (var level in new[] { GalacticAiLevel.Strong, GalacticAiLevel.Principle })
                {
                    string dir = Path.Combine(root, "solo-" + level);
                    var s = SoloGame.Start(ctx, new SoloSettings { GalacticAi = level }, 5, 6, StrongAgent.Virtual(), 0, new SaveStore(dir), "solo-m8");
                    Assert.AreEqual(level, s.Head.Ai.GalacticAiLevel, "세이브 머리에 세기");
                    var me = new SeatClient(s.Link, ctx, 0);
                    long t = 0;
                    for (int i = 0; i < 400 && (s.ViewFor(0).Phase == Phase.Setup || s.SnapshotDue); i++)
                    {
                        var c = me.FirstChoice();
                        if (c != null) me.Answer(c, me.Legal(c)[0], t);
                        t = Math.Max(t + 100, s.AnimEndMs);
                        s.Tick(t);
                    }
                    Assert.AreNotEqual(Phase.Setup, s.ViewFor(0).Phase);
                    var record = SaveStore.Load(dir);
                    Assert.AreEqual(level, record.Head.Ai.GalacticAiLevel);
                    Assert.AreEqual(level, SoloSettings.FromHead(record.Head).GalacticAi, "설정 화면을 다시 열면 그 판의 세기");
                    var again = StrongAgent.Virtual();
                    again.GalacticLevel = level == GalacticAiLevel.Strong ? GalacticAiLevel.Principle : GalacticAiLevel.Strong;
                    var back = SoloGame.Resume(ctx, record, again, 0);
                    Assert.AreEqual(level, again.GalacticLevel, $"이어하기 = 머리의 세기({level})");
                    Assert.AreSame(level == GalacticAiLevel.Strong ? again.Strong : again.Principle, again.For(SeatId.Galactic, back.ViewFor(0).Options.NonPlayerMovePolicy));
                }

                // 방: 방 설정 「강함」으로 시작 → 저장 → 다른 세기로 맞춘 새 한 벌로 이어 연다 → 모두 돌아오면 「강함」
                string roomDir = Path.Combine(root, "room");
                var cfg = Cfg(new RoomSettings { Seats = 2, Clock = ClockPreset.Off, GalacticAi = GalacticAiLevel.Strong });
                var va = StrongAgent.Virtual();
                cfg.VirtualAgent = va;
                var store = new SaveStore(roomDir);
                cfg.Store = id => store;
                var r = new Room(ctx, cfg, 2);
                Assert.AreEqual(GalacticAiLevel.Strong, va.GalacticLevel, "방 설정 「강함」을 맞췄다");
                r.Until(() => r.Host.Game.ViewFor(0).Phase != Phase.Setup && !r.Host.Game.SnapshotDue);
                var rec = SaveStore.Load(roomDir);
                Assert.AreEqual(GalacticAiLevel.Strong, rec.Head.Room.Settings.GalacticAi);
                Assert.AreEqual(GalacticAiLevel.Strong, rec.Head.Ai.GalacticAiLevel);
                var cfg2 = Cfg();
                var va2 = StrongAgent.Virtual();
                cfg2.VirtualAgent = va2;
                var r2 = new Room(ctx, net => HostSession.Resume(ctx, net, cfg2, rec, 0));
                Assert.IsTrue(r2.Host.Approve(0, Hi("owner"), 0).Ok);
                Assert.IsTrue(r2.Host.Approve(1, Hi("g1"), 0).Ok);
                Assert.AreEqual(RoomPhase.InGame, r2.Host.Phase, "모두 돌아와 다시 시작");
                Assert.AreEqual(GalacticAiLevel.Strong, va2.GalacticLevel, "이어 연 방 = 저장된 방 설정의 세기");

                // 튜토리얼: 세션에 세기 설정이 없다 — 한 벌을 꽂아도 원칙(조립 지점 TutorialMenu는 원칙 AI를 그대로 꽂는다)
                var tut = StrongAgent.Virtual();
                var run = new TutorialRunner(ctx, TutorialLoader.Load(TestData.ReadText("tutorial/main.json"), ctx, "tutorial/main.json"), tut, TutorialDataTests.TrailAfter);
                run.Begin();
                Assert.AreEqual(GalacticAiLevel.Principle, tut.GalacticLevel);
                Assert.AreSame(tut.Principle, tut.For(SeatId.Galactic, run.Session.ViewFor(0).Options.NonPlayerMovePolicy), "튜토리얼 갤럭시단 = 원칙 AI");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
