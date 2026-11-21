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
    /// 솔로 자동 저장·이어 받기(plan §4 「저장 기반·자동 저장」, 02 §7, 04 §1 S04): 찾아 둔 판(<see cref="SoloTrail"/>)을 세이브 폴더에 쓰며 돌다가 끊고,
    /// 폴더에서 다시 읽어(<see cref="SaveStore.Load"/> → <see cref="SoloGame.Resume"/>) 이어 간 판이 끊지 않은 판과 같은지 본다. 저장 목록(머리만 읽기)·깨진 파일·옛 형식 판도 여기서.
    /// 세이브는 시험마다 임시 폴더에 쓰고 지운다.
    /// </summary>
    public class SaveFlowTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const long Clock = 1794000000000;   // 2026-11-06쯤(UTC ms) — 시험의 고정 시계
        const string Hero = "bw_001_hilda";
        const int EndCommands = 60;

        static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "sinnoh-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static void Clean(params string[] dirs)
        {
            foreach (var d in dirs)
                if (d != null && Directory.Exists(d)) Directory.Delete(d, true);
        }

        /// <summary>스냅샷을 쓸 때마다 본다(쉬는 때에만 찍히나).</summary>
        sealed class WatchStore : ISaveStore
        {
            readonly ISaveStore inner;
            public readonly List<GameState> Snapshots = new List<GameState>();

            public WatchStore(ISaveStore inner)
            {
                this.inner = inner;
            }

            public void WriteHead(SaveHead head) => inner.WriteHead(head);

            public void WriteSnapshot(SaveSnapshot snapshot)
            {
                Snapshots.Add(snapshot.State.Clone());
                inner.WriteSnapshot(snapshot);
            }

            public void AppendCommand(SaveLogLine line) => inner.AppendCommand(line);
            public void TruncateLog(int commandCount) => inner.TruncateLog(commandCount);
        }

        /// <summary>좌석 0 화면 대신: 셋업은 판의 색인대로, 그 뒤는 길잡이(<see cref="SoloPilot"/>)가 답한다.</summary>
        sealed class Run
        {
            public readonly SoloTrail Trail;
            public readonly SoloPilot Pilot;
            public LocalSession S;
            public SeatClient C;
            public long Now;
            public int Answers;
            bool galactic;
            public bool Challenged;
            public readonly List<string> Asked = new List<string>();

            readonly Ctx ctx;

            public Run(Ctx ctx, SoloTrail trail)
            {
                this.ctx = ctx;
                Trail = trail;
                Pilot = trail.Pilot();
            }

            public void Attach(LocalSession session)
            {
                C?.Close();
                S = session;
                Now = 0;
                C = new SeatClient(session.Link, ctx, SoloGame.Seat);
                C.Updated += u =>
                {
                    Pilot.Saw(u.Events);
                    foreach (var e in u.Events)
                        if (e is BattleStarted b) galactic = b.Kind == BattleKind.Galactic;
                        else if (e is BattleEnded && galactic) Challenged = true;
                };
            }

            /// <summary>내 선택 하나에 답한다. 열린 내 선택이 없으면 시간을 보내고(AI 답) false.</summary>
            public bool Step()
            {
                var c = C.FirstChoice();
                if (c == null)
                {
                    Now += 60000;
                    S.Tick(Now);
                    return false;
                }
                var legal = C.Legal(c);
                ChoiceAnswer a;
                if (c.Kind == ChoiceKind.VillagePick) a = legal[Trail.Village];
                else if (c.Kind == ChoiceKind.StarterPick) a = legal[Trail.Starter];
                else if (c.Kind == ChoiceKind.TrainerCardPick) a = legal[0];
                else a = Pilot.Decide(ctx, C.View, c, legal);
                Assert.IsNotNull(a, $"길잡이가 받을 줄 모르는 선택 {c.Kind}/{c.Sub}");
                Asked.Add(c.Kind + "/" + c.Sub);
                Assert.IsTrue(C.Answer(c, a, Now).Accepted, $"{c.Kind}/{c.Sub} {a}");
                Answers++;
                return true;
            }

            /// <summary>배틀 한가운데인가: 배틀 행동을 n번 넘게 냈고 다음 배틀 행동이 열려 있다.</summary>
            public bool MidBattle(int n = 1)
            {
                var c = C.FirstChoice();
                return c != null && c.Kind == ChoiceKind.BattleAction && C.View.Battle != null && Asked.FindAll(x => x.StartsWith("BattleAction")).Count >= n;
            }

            /// <summary>길잡이가 센 것(끝난 야생 배틀 수)이 돌아가도 어긋나지 않는 자리인가: 쉬는 때이거나 배틀 한가운데(배틀 끝을 아직 세지 않았다).</summary>
            public bool Safe() => SaveRest.IsRest(S.HostCopy()) || S.HostCopy().Battle != null;

            /// <summary>
            /// 판의 끝으로 삼는 자리: [도전]까지 갔고 명령이 <see cref="EndCommands"/>개를 넘은 뒤 처음으로 내 선택이 열린 때.
            /// 내 답을 기다리는 상태는 시각과 무관하게 한 줄로 이어지므로(AI 답은 그 사이에 다 든다) 끊고 이어 한 판과 같은 자리에서 멈춘다.
            /// </summary>
            public bool AtEnd() => Challenged && S.CommandCount >= EndCommands && C.FirstChoice() != null;

            /// <summary>조건이 될 때까지 돈다.</summary>
            public void Until(Func<Run, bool> done)
            {
                for (int i = 0; i < 20000 && Answers < 800 && !done(this); i++) Step();
                Assert.IsTrue(done(this), $"{Trail.Name}: 거기까지 가지 못했다(답 {Answers}개)");
            }
        }

        LocalSession Start(SoloTrail trail, ISaveStore store, string saveId = "t", long clock = Clock, string hero = Hero) =>
            SoloGame.Start(ctx, new SoloSettings { Protagonist = hero }, trail.Seed, SoloTrail.AiSeed, new PrincipleAgent(), 0, store, saveId, savedAt: () => clock);

        LocalSession Resume(string dir, bool write = true) =>
            SoloGame.Resume(ctx, SaveStore.Load(dir), new PrincipleAgent(), 0, write ? new SaveStore(dir) : null, savedAt: () => Clock + 1);

        // 끊지 않고 간 판의 끝(해시 · 명령 수)
        void Whole(SoloTrail trail, out ulong hash, out int count)
        {
            var run = new Run(ctx, trail);
            run.Attach(SoloGame.Start(ctx, new SoloSettings(), trail.Seed, SoloTrail.AiSeed, new PrincipleAgent(), 0));
            run.Until(r => r.AtEnd());
            hash = run.S.Hash;
            count = run.S.CommandCount;
        }

        /// <summary>UB 판 시드(UB 목업·UiShots `ub-*`와 같은 판 — 솔로 시작은 좌석 하나라 보드는 다르다).</summary>
        const ulong UbSeed = 20261117;

        // 울트라 버스트 솔로 판: 셋업은 첫 합법 답, 그 뒤 내 맵 행동은 [턴 종료] — 라운드 끝마다 UB 턴이 돈다(좌석 하나). ubTurns번 돈 뒤 내 맵 행동이 열린 쉬는 때까지
        static void UbRun(LocalSession s, SeatClient c, int ubTurns, ref long now)
        {
            int seen = 0;
            c.Updated += u => { foreach (var e in u.Events) if (e is UbTurnStarted) seen++; };
            for (int i = 0; i < 20000; i++)
            {
                var ch = c.FirstChoice();
                if (seen >= ubTurns && ch != null && ch.Kind == ChoiceKind.MapAction && SaveRest.IsRest(s.HostCopy())) return;
                if (ch == null)
                {
                    now += 60000;
                    s.Tick(now);
                    continue;
                }
                var legal = c.Legal(ch);
                var a = ch.Kind == ChoiceKind.MapAction ? legal.Find(x => x.Kind == AnswerKind.EndTurn) ?? legal[0] : legal[0];
                Assert.IsTrue(c.Answer(ch, a, now).Accepted, $"{ch.Kind}/{ch.Sub} {a}");
            }
            Assert.Fail($"UB 턴 {ubTurns}번까지 가지 못했다({seen})");
        }

        [Test]
        public void M6_울트라버스트솔로판을저장하고이어받으면같은보드다_UB토큰칸과파괴된칸과셋업값()
        {
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "solo-ub");
                var set = new SoloSettings { Campaign = Campaign.UltraBurst, UltraOnslaught = true, UbDifficulty = UbDifficulty.VeteranTrainer, Protagonist = Hero };
                var s = SoloGame.Start(ctx, set, UbSeed, SoloTrail.AiSeed, new PrincipleAgent(), 0, new SaveStore(dir), "solo-ub", savedAt: () => Clock);
                var c = new SeatClient(s.Link, ctx, SoloGame.Seat);
                long now = 0;
                UbRun(s, c, 0, ref now);
                // 셋업 값(p.23 · A14 · A21): 규토리 빨강·파랑·노랑 3씩 · 파티 6칸 · 베테랑 = 축복 1 · 온슬롯 켬 · 막 없음 · UB 9 + 네크로즈마가 맵에
                var v = c.View;
                Assert.AreEqual(Campaign.UltraBurst, v.Campaign);
                Assert.IsNull(v.Hos, "울트라 버스트에는 막이 없다");
                Assert.IsNotNull(v.Ub);
                foreach (var color in new[] { ApricornColor.Red, ApricornColor.Blue, ApricornColor.Yellow })
                    Assert.That(v.Seats[0].Apricorns[(int)color], Is.GreaterThanOrEqualTo(UltraBurstSetup.StartingApricorns), color.ToString());
                Assert.AreEqual(6, v.Seats[0].OpenSlots, "파티 6칸이 모두 열려 있다");
                Assert.AreEqual(1, v.Ub.Blessings, "베테랑 = 아르세우스의 축복 1");
                Assert.IsTrue(v.Options.UltraOnslaught, "온슬롯은 울트라 버스트 판에서 고른 값 그대로");
                Assert.AreEqual(UbState.Beasts, v.Tokens.FindAll(t => t.Kind == TokenKind.UltraBeast && t.Cell >= 0).Count);
                Assert.AreEqual(1, v.Tokens.FindAll(t => t.Kind == TokenKind.Necrozma && t.Cell >= 0).Count);

                // UB 턴 넷 → 내 턴의 쉬는 때에서 끊는다(UB가 옮겨 갔고 밟은 칸이 부서졌다)
                UbRun(s, c, 4, ref now);
                var host = s.HostCopy();
                Assert.That(host.Ub.Destroyed, Is.GreaterThan(0), "네 번의 UB 턴 동안 부서진 나무·사당이 있다");

                var list = SaveStore.List(root);
                Assert.AreEqual(1, list.Count);
                var head = list[0].Head;
                Assert.IsTrue(list[0].Readable, list[0].Detail);
                Assert.AreEqual(Campaign.UltraBurst, head.Campaign);
                Assert.AreEqual(-1, head.Summary.Act, "울트라 버스트 요약에는 막이 없다");
                var again = SoloSettings.FromHead(head);
                Assert.AreEqual(Campaign.UltraBurst, again.Campaign);
                Assert.IsTrue(again.UltraOnslaught);
                Assert.AreEqual(UbDifficulty.VeteranTrainer, again.UbDifficulty);

                var back = Resume(dir, write: false);
                Assert.AreEqual(0, back.Rewound);
                Assert.AreEqual(s.Hash, back.Hash, "이어 받은 판 = 끊은 판(난수까지)");
                var after = back.HostCopy();
                Assert.AreEqual(host.Tokens.Count, after.Tokens.Count);
                for (int i = 0; i < host.Tokens.Count; i++)
                {
                    Assert.AreEqual(host.Tokens[i].Kind, after.Tokens[i].Kind, "토큰 " + i);
                    Assert.AreEqual(host.Tokens[i].Cell, after.Tokens[i].Cell, "토큰 칸 " + i + " " + host.Tokens[i].Kind);
                    Assert.AreEqual(host.Tokens[i].Destroyed, after.Tokens[i].Destroyed, "파괴 " + i);
                }
                Assert.AreEqual(host.Ub.Destroyed, after.Ub.Destroyed);
                CollectionAssert.AreEqual(host.Ub.Visited, after.Ub.Visited);
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_저장왕복_머리만읽은요약과이어받은상태가쓴판과같다()
        {
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "solo-a");
                var run = new Run(ctx, SoloTrail.Defeat);
                run.Attach(Start(SoloTrail.Defeat, new SaveStore(dir), "solo-a"));
                Assert.IsTrue(run.S.Saving);
                // 턴을 두 번 넘긴 뒤 내 턴의 쉬는 때에서 끊는다
                run.Until(r => r.C.View.Phase == Phase.Turn && r.C.View.Round >= 2 && SaveRest.IsRest(r.S.HostCopy()) && r.C.Choice(ChoiceKind.MapAction) != null);
                var host = run.S.HostCopy();

                // 목록 한 줄: 머리만(스냅샷·로그를 풀지 않는다)
                var list = SaveStore.List(root);
                Assert.AreEqual(1, list.Count);
                var e = list[0];
                Assert.IsTrue(e.Readable, e.Detail);
                Assert.AreEqual("solo-a", e.SaveId);
                Assert.AreEqual("solo-a", e.Head.SaveId);
                Assert.AreEqual(Campaign.HeartOfSinnoh, e.Head.Campaign);
                Assert.AreEqual(SoloGame.SeatCount, e.Head.SeatCount);
                CollectionAssert.AreEqual(new[] { Hero }, e.Head.Protagonists, "좌석의 주인공이 머리에");
                var sum = e.Head.Summary;
                Assert.AreEqual(Clock, sum.SavedAtMs, "저장 시각은 받은 시계에서");
                Assert.AreEqual(host.Hos.Act, sum.Act);
                Assert.That(sum.Round, Is.GreaterThanOrEqualTo(2).And.LessThanOrEqualTo(host.Round), "요약은 마지막 스냅샷(턴 끝) 때의 것");
                Assert.IsFalse(sum.Finished);
                Assert.AreEqual(1, sum.Seats.Count);
                Assert.AreEqual(host.Seats[0].HomeVillage, sum.Seats[0].HomeVillage);
                Assert.AreEqual(host.Seats[0].TrainerCard, sum.Seats[0].TrainerCard);
                Assert.That(sum.Seats[0].Party.Count, Is.GreaterThanOrEqualTo(1), "파티 요약 = 포켓몬 표 색인");
                Assert.AreEqual(host.Pokemon[host.Seats[0].Party[0]].Def, sum.Seats[0].Party[0]);

                // 머리에서 솔로 설정을 되살린다
                var set = SoloSettings.FromHead(e.Head);
                Assert.AreEqual(Hero, set.Protagonist);
                Assert.AreEqual(Campaign.HeartOfSinnoh, set.Campaign);
                Assert.IsTrue(set.TrainerCards);
                Assert.AreEqual(GalacticAiLevel.Principle, set.GalacticAi);

                // 이어 받은 상태 = 끊은 상태(난수까지)
                var back = Resume(dir, write: false);
                Assert.AreEqual(0, back.Rewound, "쉬는 때에서 끊었으면 버리는 줄이 없다");
                Assert.AreEqual(run.S.CommandCount, back.CommandCount);
                Assert.AreEqual(run.S.Hash, back.Hash);
                var again = back.HostCopy();
                Assert.AreEqual(host.Rng.State, again.Rng.State);
                Assert.AreEqual(host.Rng.Increment, again.Rng.Increment);
                Assert.AreEqual(SoloTrail.AiSeed, back.Head.Ai.Seed, "AI 난수도 머리의 것 그대로");
                Assert.IsFalse(Directory.GetFiles(dir).Length == 0);
                foreach (var f in Directory.GetFiles(dir)) Assert.IsFalse(f.EndsWith(".tmp"), "임시 파일이 남지 않는다(바꿔치기)");
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_옛규칙판저장도이어받아끝까지간다_옵션은적힌값그대로_마비대기는출전때지운다()
        {
            // 결정 72 반영(규칙 판 2 → 3) 전의 저장: 머리·스냅샷의 옵션이 옛 기본값(은하의 응징 피해 전·바로 골드·우두머리 인정·진화 불인정)이고,
            // 옛 [가정]대로 배틀 밖에서도 마비 대기가 남은 포켓몬이 있다(사용자 판 solo-20261113-… 같은 꼴, 임시 폴더의 사본으로 흉내 낸다)
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "solo-old");
                var run = new Run(ctx, SoloTrail.Defeat);
                run.Attach(Start(SoloTrail.Defeat, new SaveStore(dir), "solo-old"));
                run.Until(r => r.C.View.Phase == Phase.Turn && r.C.View.Round >= 2 && SaveRest.IsRest(r.S.HostCopy()) && r.C.Choice(ChoiceKind.MapAction) != null);

                var rec = SaveStore.Load(dir);
                void Old(RuleOptions o)
                {
                    o.GalacticRetributionTiming = GalacticRetributionTiming.BeforeDamage;
                    o.UnlockOrder = UnlockOrder.StraightToGold;
                    o.UnlockAlphaAsStrong = true;
                    o.UnlockByEvolution = false;
                }
                rec.Head.RulesVersion = 2;
                Old(rec.Head.Options);
                var snap = rec.Snapshot.State;
                Old(snap.Options);
                var p = snap.Pokemon[snap.Seats[0].Party[0]];
                p.Status = StatusEffect.Paralysed;
                p.ParalysisPending = true;
                var store = new SaveStore(dir);
                store.WriteHead(rec.Head);
                store.WriteSnapshot(rec.Snapshot);

                var back = SaveStore.Load(dir);
                Assert.That(back.Warnings, Has.Some.Contains("규칙 판"), "판이 다르면 경고하고 스냅샷에서 이어 간다");
                run.Attach(Resume(dir));
                var host = run.S.HostCopy();
                Assert.AreEqual(GalacticRetributionTiming.BeforeDamage, host.Options.GalacticRetributionTiming, "옛 판은 적힌 옵션 그대로");
                Assert.AreEqual(UnlockOrder.StraightToGold, host.Options.UnlockOrder);
                Assert.AreEqual(2, run.S.Head.RulesVersion, "머리의 판은 그대로(다음 이어 받기도 맞대 보지 않는다)");

                // 이어서 [도전](옛 옵션의 응징 피해 전)까지 끝까지 간다 — 다시 적용·새 규칙에서 어긋나지 않는다
                run.Until(r => r.AtEnd());
                var end = run.S.HostCopy();
                for (int id = 0; id < end.Pokemon.Count; id++)
                    if (end.Pokemon[id].ParalysisPending)
                        Assert.IsTrue(end.Battle != null && (end.Battle.Sides[0].Active == id || end.Battle.Sides[1].Active == id), "마비 대기는 출전 중일 때만 남는다(출전 때 지운다)");
                Assert.IsTrue(run.Challenged, "1막 조무래기 배틀(갤럭시단)을 옛 옵션으로 지났다");
                // 다시 읽어 끝까지 다시 적용해도 같은 끝
                Assert.AreEqual(run.S.Hash, LocalSession.Resume(ctx, SaveStore.Load(dir), new SessionConfig { VirtualAgent = new PrincipleAgent() }, 0).Hash);
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void G14_결정72전규칙판저장은면바꾸기순서를묻지않고이어간다_판4부터는묻는다_그값은저장에없다()
        {
            // 결정 72 선택 창은 명령 줄에 새 선택을 끼운다 — 판 4 전의 로그를 다시 적용할 때 그 선택이 끼면 선택 Id가 밀려 어긋난다.
            // 그래서 머리의 규칙 판이 4보다 낮은 저장은 그 판 동안 묻지 않는다(RuleOptions.FixedEffectOrder, 세션이 머리에서 셈 — 저장에는 없다)
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "solo-v3");
                var run = new Run(ctx, SoloTrail.Defeat);
                run.Attach(Start(SoloTrail.Defeat, new SaveStore(dir), "solo-v3"));
                run.Until(r => r.C.View.Phase == Phase.Turn && r.C.View.Round >= 2 && SaveRest.IsRest(r.S.HostCopy()) && r.C.Choice(ChoiceKind.MapAction) != null);
                Assert.IsFalse(run.S.HostCopy().Options.FixedEffectOrder, "새 판은 묻는다");
                foreach (var f in Directory.GetFiles(dir)) StringAssert.DoesNotContain("FixedEffectOrder", File.ReadAllText(f), "저장에 넣지 않는다");
                Assert.IsFalse(Resume(dir, false).HostCopy().Options.FixedEffectOrder, "지금 판(6) 저장은 묻는다 — 판 4 이상이면 묻는다");

                var rec = SaveStore.Load(dir);
                rec.Head.RulesVersion = SaveRecord.EffectOrderRulesVersion - 1;
                new SaveStore(dir).WriteHead(rec.Head);
                run.Attach(Resume(dir));
                Assert.IsTrue(run.S.HostCopy().Options.FixedEffectOrder, "판 3 저장은 그 판 동안 묻지 않는다");
                // 이어서 [도전]까지 끝까지 간다 — 판 3으로 이어 간 판도 어긋나지 않는다
                run.Until(r => r.AtEnd());
                Assert.AreEqual(run.S.Hash, LocalSession.Resume(ctx, SaveStore.Load(dir), new SessionConfig { VirtualAgent = new PrincipleAgent() }, 0).Hash);
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_찾아둔판을여러곳에서끊고이어하면끊지않은판과끝이같다_스냅샷은쉬는때에만()
        {
            Whole(SoloTrail.Defeat, out ulong final, out int finalCount);
            string root = TempDir();
            try
            {
                // 답 4·7·10·25개 뒤, 그리고 첫 야생 배틀과 배틀 뒤 선택이 끝난 뒤에 끊는다. 쉬는 때가 아니면 그 앞으로 돌아간다 —
                // 길잡이는 끝난 야생 배틀 수를 따로 세므로(돌아가지 않는다) 배틀 끝과 쉬는 때 사이에서는 끊지 않는다
                var cuts = new Func<Run, bool>[]
                {
                    r => r.Answers >= 4 && r.Safe(), r => r.Answers >= 7 && r.Safe(), r => r.Answers >= 10 && r.Safe(), r => r.Answers >= 25 && r.Safe(),
                    r => r.Pilot.Hunted >= 1 && SaveRest.IsRest(r.S.HostCopy()),
                };
                for (int k = 0; k < cuts.Length; k++)
                {
                    string dir = SaveStore.PathOf(root, "cut" + k);
                    var watch = new WatchStore(new SaveStore(dir));
                    var run = new Run(ctx, SoloTrail.Defeat);
                    run.Attach(Start(SoloTrail.Defeat, watch));
                    run.Until(cuts[k]);
                    foreach (var snap in watch.Snapshots) Assert.IsTrue(SaveRest.IsRest(snap), $"끊기 {k}: 스냅샷 {snap.CommandCount}이 쉬는 때가 아니다");
                    Assert.Less(run.S.CommandCount, finalCount, $"끊기 {k}: 끝보다 앞에서 끊는다");
                    if (k == 4)
                    {
                        Assert.That(run.Asked, Does.Contain("Reward/TeachMove"), "배틀 뒤 선택을 지났다");
                        Assert.That(watch.Snapshots.Count, Is.GreaterThanOrEqualTo(2), "게임 시작 + 배틀 뒤 선택에 답한 뒤(배틀 끝에서는 찍지 않는다)");
                    }

                    // 세션을 버리고 폴더에서 이어 받는다 — 같은 길잡이가 이어서 답한다
                    run.Attach(Resume(dir));
                    Assert.IsTrue(SaveRest.IsRest(run.S.HostCopy()), $"끊기 {k}: 이어 받은 자리는 쉬는 때");
                    Assert.IsEmpty(run.S.Warnings);
                    run.Until(r => r.AtEnd());
                    Assert.AreEqual(finalCount, run.S.CommandCount, $"끊기 {k}: 명령 수");
                    Assert.AreEqual(final, run.S.Hash, $"끊기 {k}: 이어 간 판의 끝 해시");

                    // 이어 쓴 세이브도 성하다: 로그 Seq가 이어지고 다시 읽어도 같은 끝
                    var rec = SaveStore.Load(dir);
                    Assert.IsEmpty(rec.Warnings, string.Join(" / ", rec.Warnings));
                    Assert.AreEqual(finalCount, SaveStore.ReadLog(dir).Count);
                    Assert.AreEqual(final, LocalSession.Resume(ctx, rec, new SessionConfig { VirtualAgent = new PrincipleAgent() }, 0).Hash);
                    Assert.AreEqual(Clock + 1, SaveStore.Peek(dir).Head.Summary.SavedAtMs, "이어 쓴 뒤의 저장 시각");
                }
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_배틀한가운데에서끊기면그앞의쉬는때로돌아가고이어한끝은같다()
        {
            Whole(SoloTrail.Defeat, out ulong final, out int finalCount);
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "battle");
                var run = new Run(ctx, SoloTrail.Defeat);
                run.Attach(Start(SoloTrail.Defeat, new SaveStore(dir)));
                // 야생 배틀에서 기술을 한 번 쓴 뒤(배틀 행동이 다시 열렸다)
                run.Until(r => r.MidBattle());
                int cut = run.S.CommandCount;
                Assert.IsNotNull(run.S.HostCopy().Battle);
                Assert.AreEqual(cut, SaveStore.ReadLog(dir).Count, "명령마다 로그 한 줄 — 배틀 한가운데도 쓰여 있다");
                var rec = SaveStore.Load(dir);
                Assert.IsNull(rec.Snapshot.State.Battle, "스냅샷은 배틀 밖");
                Assert.That(SaveStore.Peek(dir).Head.Summary.Seats[0].Party.Count, Is.GreaterThanOrEqualTo(1), "셋업이 끝난 때에도 스냅샷과 머리를 쓴다(목록의 파티 요약)");

                run.Attach(Resume(dir));
                var host = run.S.HostCopy();
                Assert.That(run.S.Rewound, Is.GreaterThan(0), "배틀 한가운데의 줄은 버린다");
                Assert.AreEqual(cut - run.S.Rewound, run.S.CommandCount);
                Assert.IsNull(host.Battle, "배틀 앞으로 돌아왔다");
                Assert.IsTrue(SaveRest.IsRest(host));
                Assert.IsNotNull(run.C.Choice(ChoiceKind.MapAction), "보드에서 행동을 고르는 때");
                Assert.AreEqual(run.S.CommandCount, SaveStore.ReadLog(dir).Count, "버린 줄은 로그에서도 잘라 냈다");

                // 같은 답을 다시 내면 같은 판이다(난수는 돌아간 자리의 것)
                run.Until(r => r.AtEnd());
                Assert.AreEqual(finalCount, run.S.CommandCount);
                Assert.AreEqual(final, run.S.Hash);
                Assert.IsEmpty(SaveStore.Load(dir).Warnings);

                // 로그에 쓰지 않는 이어 받기(store 없음)는 파일을 건드리지 않는다
                string dir2 = SaveStore.PathOf(root, "battle2");
                var run2 = new Run(ctx, SoloTrail.Defeat);
                run2.Attach(Start(SoloTrail.Defeat, new SaveStore(dir2)));
                run2.Until(r => r.MidBattle());
                int lines = SaveStore.ReadLog(dir2).Count;
                Assert.That(Resume(dir2, write: false).Rewound, Is.GreaterThan(0));
                Assert.AreEqual(lines, SaveStore.ReadLog(dir2).Count);
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_셋업묶음을반만답하고끊기면묶음앞으로돌아간다()
        {
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "setup");
                var run = new Run(ctx, SoloTrail.Defeat);
                run.Attach(Start(SoloTrail.Defeat, new SaveStore(dir)));
                run.Until(r => r.Answers >= 2);   // 고향 마을 → 스타팅 포켓몬(트레이너 카드는 아직)
                Assert.IsNull(run.C.Choice(ChoiceKind.StarterPick));
                Assert.IsNotNull(run.C.Choice(ChoiceKind.TrainerCardPick));
                Assert.IsFalse(SaveRest.IsRest(run.S.HostCopy()), "반쯤 답한 묶음은 쉬는 때가 아니다");

                run.Attach(Resume(dir));
                Assert.AreEqual(1, run.S.Rewound);
                Assert.AreEqual(Phase.Setup, run.C.View.Phase);
                Assert.IsNotNull(run.C.Choice(ChoiceKind.StarterPick), "묶음의 두 선택이 다 열려 있다(셋업 화면이 그대로 받는다)");
                Assert.IsNotNull(run.C.Choice(ChoiceKind.TrainerCardPick));
                Assert.AreEqual(1, SaveStore.ReadLog(dir).Count);
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_깨진저장과옛형식판은목록에서읽을수없음으로걸러지고던지지않는다()
        {
            string root = TempDir();
            try
            {
                // 성한 저장 둘(저장 시각이 다르다)
                foreach (var (id, clock) in new[] { ("solo-old", Clock), ("solo-new", Clock + 5000) })
                {
                    var run = new Run(ctx, SoloTrail.Defeat);
                    run.Attach(Start(SoloTrail.Defeat, new SaveStore(SaveStore.PathOf(root, id)), id, clock));
                    run.Until(r => r.Answers >= 4);
                }
                string good = File.ReadAllText(Path.Combine(root, "solo-old", SaveStore.HeadFile));
                string snap = Directory.GetFiles(Path.Combine(root, "solo-old"), "snap-*.json")[0];
                void Make(string id, string head, bool withSnap = true)
                {
                    string d = Path.Combine(root, id);
                    Directory.CreateDirectory(d);
                    if (head != null) File.WriteAllText(Path.Combine(d, SaveStore.HeadFile), head);
                    if (withSnap) File.Copy(snap, Path.Combine(d, Path.GetFileName(snap)));
                }
                string Format(int v)
                {
                    string now = "\"FormatVersion\": " + SaveRecord.CurrentFormat;
                    Assert.That(good, Does.Contain(now));
                    return good.Replace(now, "\"FormatVersion\": " + v);
                }
                Make("x-cut", good.Substring(0, good.Length / 2));           // 쓰다 끊긴 머리
                Make("x-empty", "");                                         // 빈 파일
                Make("x-null", "null");
                Make("x-nohead", null);                                      // 머리 없음
                Make("x-nosnap", good, withSnap: false);                     // 스냅샷 없음
                Make("x-old", Format(SaveRecord.CurrentFormat - 1));         // 옛 형식 판
                // 새 형식 판: 형식을 올리는 까닭이 필드가 느는 것이라 모르는 필드가 함께 온다 — 「깨짐」이 아니라 「다른 버전」으로 가른다
                Make("x-new", Format(SaveRecord.CurrentFormat + 1).Replace("\"SaveId\"", "\"Mystery\": 1, \"SaveId\""));
                Make("x-new-bare", Format(SaveRecord.CurrentFormat + 1));    // 판 숫자만 다른 머리
                Make("x-field", good.Replace("\"SaveId\"", "\"Mystery\": 1, \"SaveId\""));   // 모르는 필드(다른 판의 머리)
                Make("x-enum", good.Replace("\"Campaign\": 0", "\"Campaign\": \"없는값\""));   // 값이 맞지 않는다
                File.WriteAllText(Path.Combine(root, "stray.txt"), "폴더가 아닌 것");

                List<SaveEntry> list = null;
                Assert.DoesNotThrow(() => list = SaveStore.List(root));
                Assert.AreEqual(12, list.Count);
                Assert.AreEqual("solo-new", list[0].SaveId, "새것부터");
                Assert.AreEqual("solo-old", list[1].SaveId);
                Assert.IsTrue(list[0].Readable && list[1].Readable);
                var bad = new Dictionary<string, SaveProblem>();
                for (int i = 2; i < list.Count; i++)
                {
                    Assert.IsFalse(list[i].Readable, list[i].SaveId);
                    Assert.IsNull(list[i].Head);
                    Assert.IsNotEmpty(list[i].Detail);
                    bad[list[i].SaveId] = list[i].Problem;
                }
                Assert.AreEqual(SaveProblem.OldFormat, bad["x-old"]);
                Assert.AreEqual(SaveProblem.NewFormat, bad["x-new"], "필드가 는 새 형식 머리");
                Assert.AreEqual(SaveProblem.NewFormat, bad["x-new-bare"]);
                foreach (string id in new[] { "x-cut", "x-empty", "x-null", "x-nohead", "x-nosnap", "x-field", "x-enum" }) Assert.AreEqual(SaveProblem.Broken, bad[id], id);

                // 읽기(이어 받기)는 그 저장만 거절한다
                Assert.Throws<NotSupportedException>(() => SaveStore.Load(Path.Combine(root, "x-old")), "옛 형식 판을 옮기는 길은 없다");
                Assert.Throws<NotSupportedException>(() => SaveStore.Load(Path.Combine(root, "x-new")));
                Assert.Catch(() => SaveStore.Load(Path.Combine(root, "x-cut")));
                Assert.Catch(() => SaveStore.Load(Path.Combine(root, "x-nosnap")));
                var oldHead = SaveStore.Load(Path.Combine(root, "solo-old"));
                oldHead.Head.FormatVersion = SaveRecord.CurrentFormat - 1;
                Assert.Throws<NotSupportedException>(() => SoloGame.Resume(ctx, oldHead, new PrincipleAgent(), 0));

                // 솔로가 아닌 머리 · 없는 폴더 · 지우기 · 폴더 이름이 아닌 저장 이름
                var two = SaveStore.Load(Path.Combine(root, "solo-old"));
                two.Head.SeatCount = 2;
                Assert.Throws<NotSupportedException>(() => SoloGame.Resume(ctx, two, new PrincipleAgent(), 0));
                Assert.IsEmpty(SaveStore.List(Path.Combine(root, "없는폴더")));
                Assert.IsEmpty(SaveStore.List(null));
                Assert.IsTrue(SaveStore.Delete(root, "x-cut"));
                Assert.IsFalse(SaveStore.Delete(root, "x-cut"));
                Assert.IsFalse(Directory.Exists(Path.Combine(root, "x-cut")));
                Assert.AreEqual(11, SaveStore.List(root).Count);
                foreach (string id in new[] { "..", ".", "", "a/b", "a\\b", "c:x", null })
                    Assert.Throws<ArgumentException>(() => SaveStore.Delete(root, id), id ?? "null");
                Assert.IsTrue(Directory.Exists(root));
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_가운데가깨진로그를이어받으면그뒤를잘라내어이어쓴줄이다음에도읽힌다()
        {
            Whole(SoloTrail.Defeat, out ulong final, out int finalCount);
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "midtorn");
                var run = new Run(ctx, SoloTrail.Defeat);
                run.Attach(Start(SoloTrail.Defeat, new SaveStore(dir)));
                run.Until(r => r.Answers >= 8 && SaveRest.IsRest(r.S.HostCopy()) && SaveStore.Load(dir).Snapshot.CommandCount + 2 <= r.S.CommandCount);
                int cut = run.S.CommandCount;
                // 로그의 마지막 줄을 깨진 줄로 바꾼다(줄바꿈은 그대로 — 쓰다 끊긴 꼬리가 아니라 가운데가 깨진 파일) + 그 뒤에 성해 보이는 줄 하나
                string path = Path.Combine(dir, SaveStore.LogFile);
                var lines = new List<string>(File.ReadAllText(path).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
                Assert.AreEqual(cut, lines.Count);
                string tail = lines[cut - 1];
                lines[cut - 1] = "{x";
                lines.Add(tail);
                File.WriteAllText(path, string.Join("\n", lines) + "\n");
                var torn = SaveStore.Load(dir);
                Assert.That(torn.Warnings, Has.Some.Contains("깨져"), "읽기는 깨진 줄 앞까지만 쓴다");
                Assert.That(torn.Snapshot.CommandCount + torn.Commands.Count, Is.LessThan(cut));

                run.Attach(Resume(dir));
                Assert.That(run.S.CommandCount, Is.LessThan(cut), "깨진 줄 앞으로 돌아왔다");
                Assert.AreEqual(run.S.CommandCount, SaveStore.ReadLog(dir).Count, "깨진 줄과 그 뒤는 파일에서도 잘라 냈다");
                Assert.IsEmpty(SaveStore.Load(dir).Warnings);

                // 이어 쓴 줄마다 다시 읽힌다(깨진 줄 뒤에 붙어 버려지지 않는다)
                for (int i = 0; i < 6; i++)
                {
                    run.Step();
                    var rec = SaveStore.Load(dir);
                    Assert.IsEmpty(rec.Warnings, string.Join(" / ", rec.Warnings));
                    Assert.AreEqual(run.S.CommandCount, rec.Snapshot.CommandCount + rec.Commands.Count, $"이어 쓴 뒤 {i}: 읽힌 명령 수");
                }
                run.Until(r => r.AtEnd());
                Assert.AreEqual(finalCount, run.S.CommandCount);
                Assert.AreEqual(final, run.S.Hash);
                Assert.AreEqual(final, LocalSession.Resume(ctx, SaveStore.Load(dir), new SessionConfig { VirtualAgent = new PrincipleAgent() }, 0).Hash, "다시 읽어도 같은 끝");
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_로그자르기는깨진줄과Seq가겹친줄에서끊는다()
        {
            string root = TempDir();
            try
            {
                SaveLogLine Line(int seq) => new SaveLogLine { Seq = seq, Seat = 0, Command = Command.TimeoutOf(seq) };
                string Text(params int[] seqs)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (int q in seqs) sb.Append(q == -1 ? "{x" : q == -2 ? "{\"Seq\":9,\"Seat\":0,\"Command\":null}" : SaveStore.Serialize(Line(q), false)).Append('\n');
                    return sb.ToString();
                }
                int Kept(string id, string text, int count)
                {
                    string d = SaveStore.PathOf(root, id);
                    var store = new SaveStore(d);
                    File.WriteAllText(Path.Combine(d, SaveStore.LogFile), text);
                    store.TruncateLog(count);
                    store.AppendCommand(Line(count));
                    var log = SaveStore.ReadLog(d);
                    Assert.AreEqual(count, log[log.Count - 1].Seq, id + ": 덧붙인 줄이 읽힌다");
                    return log.Count - 1;
                }
                Assert.AreEqual(4, Kept("plain", Text(0, 1, 2, 3, 4, 5), 4), "Seq가 그 수 이상인 줄 앞에서");
                Assert.AreEqual(2, Kept("broken", Text(0, 1, -1, 3, 4), 5), "깨진 줄에서");
                Assert.AreEqual(2, Kept("nocommand", Text(0, 1, -2, 3), 5), "명령이 없는 줄에서");
                Assert.AreEqual(3, Kept("dup", Text(0, 1, 2, 2, 3), 5), "Seq가 겹친 줄에서");
                Assert.AreEqual(3, Kept("back", Text(0, 1, 2, 1, 2, 3), 5), "Seq가 뒤로 간 줄에서");
                Assert.AreEqual(5, Kept("gap", Text(0, 1, 2, 8, 9), 10), "앞쪽이 잘린 로그(빈칸)는 둔다 — 스냅샷 뒤 줄만 이어지면 읽힌다");
                Assert.AreEqual(3, Kept("tail", Text(0, 1, 2) + "{\"Seq\":3,\"Se", 5), "쓰다 끊긴 꼬리");
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_어긋난로그줄은조용히버리지않고이어받기를거절한다_파일은그대로()
        {
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "diverged");
                var run = new Run(ctx, SoloTrail.Defeat);
                run.Attach(Start(SoloTrail.Defeat, new SaveStore(dir)));
                run.Until(r => r.MidBattle());
                var rec = SaveStore.Load(dir);
                Assert.That(rec.Commands.Count, Is.GreaterThanOrEqualTo(3), "스냅샷 뒤에 줄이 있다");
                // 스냅샷 뒤 첫 줄을 합법이 아닌 답(없는 선택)으로 바꾼다. 그 뒤의 줄은 성하다
                string path = Path.Combine(dir, SaveStore.LogFile);
                var lines = new List<string>(File.ReadAllText(path).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
                int at = rec.Snapshot.CommandCount;
                var bad = SaveStore.Deserialize<SaveLogLine>(lines[at]);
                Assert.AreEqual(at, bad.Seq);
                bad.Command.ChoiceId = 99999;
                lines[at] = SaveStore.Serialize(bad, false);
                string text = string.Join("\n", lines) + "\n";
                File.WriteAllText(path, text);
                Assert.IsEmpty(SaveStore.Load(dir).Warnings, "파일 꼴은 성하다(줄마다 읽힌다)");

                var ex = Assert.Throws<InvalidOperationException>(() => Resume(dir), "어긋난 줄 앞의 쉬는 때로 조용히 돌아가지 않는다");
                Assert.That(ex.Message, Does.Contain("거절"));
                Assert.AreEqual(text, File.ReadAllText(path), "거절한 이어 받기는 로그를 자르지 않는다");
                Assert.Throws<InvalidOperationException>(() => Resume(dir, write: false));
                Assert.Throws<InvalidOperationException>(() => LocalSession.Resume(ctx, SaveStore.Load(dir), new SessionConfig { VirtualAgent = new PrincipleAgent() }, 0), "끝까지 다시 적용하는 길도 같다");
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_숨겨진장소물음이열린채끊기면버리는줄없이그물음그대로이어받는다()
        {
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "spot");
                var run = new Run(ctx, SoloTrail.Defeat);
                run.Attach(Start(SoloTrail.Defeat, new SaveStore(dir)));
                // 내 턴마다: 걸어 들어갈 수 있는 뒷면 숨겨진 장소가 있으면 거기로 걷고(멈춰서 공개할지 묻는다), 없으면 턴을 넘긴다
                for (int turn = 0; turn < 20 && run.C.Choice(ChoiceKind.HiddenSpot) == null; turn++)
                {
                    run.Until(r => r.C.Choice(ChoiceKind.MapAction) != null);
                    var map = run.C.Choice(ChoiceKind.MapAction);
                    var legal = run.C.Legal(map);
                    var view = run.C.View;
                    var go = legal.Find(a => a.Kind == AnswerKind.Move && view.Tokens.Exists(t => t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell == a.Target))
                        ?? legal.Find(a => a.Kind == AnswerKind.EndTurn);
                    Assert.IsTrue(run.C.Answer(map, go, run.Now).Accepted, go.Kind.ToString());
                    while (run.C.FirstChoice() == null) run.Step();
                }
                var open = run.C.Choice(ChoiceKind.HiddenSpot);
                Assert.IsNotNull(open, "숨겨진 장소 물음까지 가지 못했다");
                var host = run.S.HostCopy();
                Assert.IsTrue(SaveRest.IsRest(host), "숨겨진 장소 물음이 열린 때는 쉬는 때다");
                Assert.Less(SaveStore.Load(dir).Snapshot.CommandCount, run.S.CommandCount, "마지막 스냅샷 뒤에 걸은 줄이 있다(로그를 다시 적용해 닿는 자리)");

                var back = new Run(ctx, SoloTrail.Defeat);
                back.Attach(Resume(dir, write: false));
                Assert.AreEqual(0, back.S.Rewound, "버리는 줄이 없다");
                Assert.AreEqual(run.S.CommandCount, back.S.CommandCount);
                Assert.AreEqual(run.S.Hash, back.S.Hash);
                var again = back.C.Choice(ChoiceKind.HiddenSpot);
                Assert.IsNotNull(again, "같은 물음이 열려 있다");
                Assert.AreEqual(open.Id, again.Id);
                Assert.IsNull(back.C.Choice(ChoiceKind.MapAction), "물음에 답하기 전에는 보드 행동이 열리지 않는다");
                Assert.AreEqual(run.S.CommandCount, SaveStore.ReadLog(dir).Count);

                // 두 판에 같은 답(공개)을 내면 같은 상태다 — 난수도 그때 그대로
                var accept = ChoiceAnswer.Of(AnswerKind.Accept);
                Assert.IsTrue(run.C.Answer(open, accept, run.Now).Accepted);
                Assert.IsTrue(back.C.Answer(again, accept, back.Now).Accepted);
                Assert.AreEqual(run.S.Hash, back.S.Hash, "공개한 뒤");
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void G12_결정71_고른길로걷다멈춘숨겨진장소물음이끊겨도남은길그대로이어받고_지나가면도착칸까지()
        {
            // 고른 길(Move + Values)이 로그에 그대로 적히고, 길 가운데의 물음(Options = 남은 걸음 칸)이 이어 받은 판에서도 같다
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "walk");
                var run = new Run(ctx, SoloTrail.Defeat);
                run.Attach(Start(SoloTrail.Defeat, new SaveStore(dir)));
                int dest = -1;
                for (int turn = 0; turn < 20 && run.C.Choice(ChoiceKind.HiddenSpot) == null; turn++)
                {
                    run.Until(r => r.C.Choice(ChoiceKind.MapAction) != null);
                    var map = run.C.Choice(ChoiceKind.MapAction);
                    var view = run.C.View;
                    var me = view.Seats[SoloGame.Seat];
                    ChoiceAnswer go = null;
                    // 뒷면 숨겨진 장소를 거쳐 그 너머 칸으로 가는 길(경유 칸 = 숨겨진 장소)
                    foreach (var t in view.Tokens)
                    {
                        if (go != null || t.Kind != TokenKind.HiddenSpot || t.FaceUp || t.Destroyed || t.Cell < 0 || t.Cell == me.Cell) continue;
                        for (int d = 0; d < Board.Directions && go == null; d++)
                        {
                            int b = ctx.Board.Neighbor(t.Cell, d);
                            if (b == Board.NoCell || b == me.Cell) continue;
                            var route = MoveRules.Route(ctx.Board, me.Cell, new[] { t.Cell }, b, me.MovesLeft, view.Options.ElevationMode);
                            if (route != null && MapActions.StopsOn(view, ctx, SoloGame.Seat, route.GetRange(1, route.Count - 1))[0] == t.Cell && route.IndexOf(t.Cell) < route.Count - 1)
                            {
                                go = ChoiceAnswer.MoveAlong(route);
                                dest = b;
                            }
                        }
                    }
                    go = go ?? run.C.Legal(map).Find(a => a.Kind == AnswerKind.EndTurn);
                    Assert.IsTrue(run.C.Answer(map, go, run.Now).Accepted, go.ToString());
                    while (run.C.FirstChoice() == null) run.Step();
                }
                var open = run.C.Choice(ChoiceKind.HiddenSpot);
                Assert.IsNotNull(open, "길 가운데의 숨겨진 장소 물음까지 가지 못했다");
                Assert.Greater(open.Options.Count, 0, "남은 길이 물음에 있다");
                Assert.AreEqual(dest, open.Options[open.Options.Count - 1]);

                var back = new Run(ctx, SoloTrail.Defeat);
                back.Attach(Resume(dir, write: false));
                Assert.AreEqual(0, back.S.Rewound);
                Assert.AreEqual(run.S.Hash, back.S.Hash);
                var again = back.C.Choice(ChoiceKind.HiddenSpot);
                CollectionAssert.AreEqual(open.Options, again.Options, "남은 길 그대로");
                var pass = ChoiceAnswer.Of(AnswerKind.Decline);
                Assert.IsTrue(run.C.Answer(open, pass, run.Now).Accepted);
                Assert.IsTrue(back.C.Answer(again, pass, back.Now).Accepted);
                Assert.AreEqual(run.S.Hash, back.S.Hash, "지나간 뒤");
                Assert.AreEqual(dest, back.S.HostCopy().Seats[SoloGame.Seat].Cell, "남은 길을 이어 걸어 도착 칸에");
            }
            finally
            {
                Clean(root);
            }
        }

        [Test]
        public void M4_깨진최신스냅샷과잘린마지막줄이있어도이어받는다()
        {
            Whole(SoloTrail.Defeat, out ulong final, out int finalCount);
            string root = TempDir();
            try
            {
                string dir = SaveStore.PathOf(root, "torn");
                var run = new Run(ctx, SoloTrail.Defeat);
                run.Attach(Start(SoloTrail.Defeat, new SaveStore(dir)));
                run.Until(r => r.Answers >= 8 && SaveRest.IsRest(r.S.HostCopy()));
                int cut = run.S.CommandCount;
                ulong hash = run.S.Hash;
                File.AppendAllText(Path.Combine(dir, SaveStore.LogFile), "{\"Seq\":" + cut + ",\"Seat\":0,\"Comm");
                File.WriteAllText(Path.Combine(dir, "snap-" + (cut + 1) + ".json"), "{ 깨짐");
                Assert.IsTrue(SaveStore.Peek(dir).Readable, "목록은 머리만 본다");

                run.Attach(Resume(dir));
                Assert.AreEqual(cut, run.S.CommandCount);
                Assert.AreEqual(hash, run.S.Hash);
                File.Delete(Path.Combine(dir, "snap-" + (cut + 1) + ".json"));
                run.Until(r => r.AtEnd());
                Assert.AreEqual(finalCount, run.S.CommandCount);
                Assert.AreEqual(final, run.S.Hash);
            }
            finally
            {
                Clean(root);
            }
        }

        // ── 무르기(M10 결정 149 · 154, undo.md §4): 저장을 거꾸로 고친다 — 스냅샷 먼저(뒤 스냅샷 지움) → 로그 자르기(올림 표시 지움) ──

        static List<int> SnapNumbers(string dir)
        {
            var r = new List<int>();
            foreach (var p in Directory.GetFiles(dir, "snap-*.json"))
            {
                string n = Path.GetFileNameWithoutExtension(p).Substring("snap-".Length);
                r.Add(int.Parse(n));
            }
            r.Sort();
            return r;
        }

        SaveSnapshot Snap(int count)
        {
            var s = Engine.NewGame(ctx, new RuleOptions(), 1, 5);
            s.CommandCount = count;
            return new SaveSnapshot { CommandCount = count, State = s };
        }

        static SaveLogLine Line(int seq) => new SaveLogLine { Seq = seq, Seat = 0, Command = Command.TimeoutOf(seq) };

        [Test]
        public void M10_무르기_스냅샷쓰기는뒤의스냅샷을지운다()
        {
            string dir = TempDir();
            try
            {
                var store = new SaveStore(dir);
                foreach (int n in new[] { 10, 20, 30 }) store.WriteSnapshot(Snap(n));
                CollectionAssert.AreEqual(new[] { 20, 30 }, SnapNumbers(dir), "평소: 최근 2개");
                store.WriteSnapshot(Snap(25));
                CollectionAssert.AreEqual(new[] { 20, 25 }, SnapNumbers(dir), "되돌린 자리보다 뒤(버린 미래)는 지운다");
                store.WriteSnapshot(Snap(20));
                CollectionAssert.AreEqual(new[] { 20 }, SnapNumbers(dir), "같은 자리는 덮어쓴다");
                store.WriteSnapshot(Snap(5));
                CollectionAssert.AreEqual(new[] { 5 }, SnapNumbers(dir));
                store.WriteSnapshot(Snap(40));
                CollectionAssert.AreEqual(new[] { 5, 40 }, SnapNumbers(dir), "다시 앞으로 가면 평소대로");
            }
            finally
            {
                Clean(dir);
            }
        }

        [Test]
        public void M10_무르기_로그를잘라도올림표시는남는다()
        {
            // 검토 반영: 표시를 지우면 「이 판을 올린 적이 있다」를 잊어 갤럭시단 배틀이 없는 이어진 갈래가 서버의 버린 갈래를 덮지 못한다 — 표시는 두고 Pack이 지문으로 견준다
            string dir = TempDir();
            try
            {
                var store = new SaveStore(dir);
                for (int i = 0; i < 10; i++) store.AppendCommand(Line(i));
                PlayRecord.MarkUploaded(dir, 10, PlayRecord.Fingerprint(SaveStore.ReadLog(dir)));
                store.TruncateLog(10);
                Assert.AreEqual(10, PlayRecord.Uploaded(dir), "자른 줄이 없으면 그대로");
                store.TruncateLog(6);
                Assert.AreEqual(6, SaveStore.ReadLog(dir).Count);
                Assert.AreEqual(10, PlayRecord.Uploaded(dir), "자른 뒤에도 올린 그대로(10줄) — 남은 6줄과 달라 다음 Pack이 다시 올린다");
                File.Delete(Path.Combine(dir, SaveStore.LogFile));
                store.TruncateLog(3);
                Assert.AreEqual(10, PlayRecord.Uploaded(dir), "로그가 없어도 그대로");
            }
            finally
            {
                Clean(dir);
            }
        }

        [Test]
        public void M10_무르기_올리는중에되돌려도이어진갈래를다시올린다()
        {
            // 검토 반영(결정 149): (가) 올리기가 끝나 표시를 적을 때 그새 로그가 잘렸어도 지문이 달라 다음 Pack이 다시 올린다 ·
            // (나) 이어진 갈래에 갤럭시단 배틀이 없어도 이 판을 올린 적이 있으면 같은 이름으로 다시 올려 서버의 버린 갈래를 덮는다. 올리기 창구는 부르지 않는다(Pack · MarkUploaded만)
            string dir = TempDir();
            try
            {
                var store = new SaveStore(dir);
                store.WriteHead(new SaveHead { Seed = 0x149 });
                store.WriteSnapshot(Snap(0));
                for (int i = 0; i < 10; i++) store.AppendCommand(new SaveLogLine { Seq = i, Seat = i == 7 ? SeatId.Galactic : 0, Command = Command.TimeoutOf(i) });
                var pack = PlayRecord.Pack(dir, "1");
                Assert.IsNotNull(pack.Bytes, "갤럭시단 줄이 있는 판");
                Assert.AreEqual(10, pack.Lines);
                // 올리는 사이에 결과 화면에서 되돌림: 갤럭시단 줄(7) 앞 6줄로 잘리고 새 갈래가 다시 10줄까지 — 그 뒤에 늦게 끝난 올리기가 옛 갈래를 적는다
                store.TruncateLog(6);
                for (int i = 6; i < 10; i++) store.AppendCommand(new SaveLogLine { Seq = i, Seat = 0, Command = Command.TimeoutOf(100 + i) });
                PlayRecord.MarkUploaded(dir, pack.Lines, pack.Fingerprint);
                var lines = SaveStore.ReadLog(dir);
                Assert.AreEqual(10, lines.Count);
                Assert.IsFalse(PlayRecord.HadGalacticBattle(lines), "이어진 갈래에는 갤럭시단 배틀이 없다");
                var again = PlayRecord.Pack(dir, "1");
                Assert.IsNotNull(again.Bytes, $"같은 줄 수라도 갈래가 다르면 다시 올린다(Skip {again.Skip})");
                Assert.AreEqual(pack.FileName, again.FileName, "같은 이름 — 서버의 버린 갈래를 덮어쓴다");
                var file = PlayRecord.Unpack(again.Bytes);
                Assert.AreEqual(10, file.Lines.Count);
                Assert.IsFalse(PlayRecord.HadGalacticBattle(file.Lines), "올라간 것은 이어진 갈래");
                PlayRecord.MarkUploaded(dir, again.Lines, again.Fingerprint);
                Assert.AreEqual("uploaded", PlayRecord.Pack(dir, "1").Skip, "올린 그대로면 다시 올리지 않는다");
                // 올린 적이 없는 판은 여전히 갤럭시단 배틀이 있어야 올린다
                File.Delete(Path.Combine(dir, PlayRecord.MarkFile));
                Assert.AreEqual("no-galactic", PlayRecord.Pack(dir, "1").Skip);
            }
            finally
            {
                Clean(dir);
            }
        }

        [Test]
        public void M10_무르기_쓰기스레드로도같다()
        {
            string dir = TempDir();
            try
            {
                using (var writer = new SaveWriter(dir))
                {
                    for (int i = 0; i < 12; i++) writer.AppendCommand(Line(i));
                    writer.WriteSnapshot(Snap(6));
                    writer.WriteSnapshot(Snap(12));
                    Assert.IsTrue(writer.Flush(SaveWriter.DisposeWaitMs));
                    PlayRecord.MarkUploaded(dir, 12);
                    // 세션의 되돌림 순서: 스냅샷(받은 순서대로 쓰기 스레드) → 로그 자르기(밀린 쓰기를 다 쓴 뒤 부른 스레드에서)
                    writer.WriteSnapshot(Snap(8));
                    writer.TruncateLog(8);
                    CollectionAssert.AreEqual(new[] { 6, 8 }, SnapNumbers(dir), "뒤 스냅샷(12)을 지웠다");
                    Assert.AreEqual(8, SaveStore.ReadLog(dir).Count);
                    Assert.AreEqual(12, PlayRecord.Uploaded(dir), "올림 표시는 그대로(지문으로 Pack이 견준다)");
                    writer.AppendCommand(Line(8));
                }
                var log = SaveStore.ReadLog(dir);
                Assert.AreEqual(9, log.Count);
                for (int i = 0; i < log.Count; i++) Assert.AreEqual(i, log[i].Seq, "잘린 뒤 이어 쓴 줄이 이어진다");
            }
            finally
            {
                Clean(dir);
            }
        }

        [Test]
        public void M10_무르기_되돌린저장을이어하면되돌린판이다()
        {
            string dir = TempDir();
            try
            {
                var agents = new IAgent[] { new Playout.BotAgent(77), new Playout.BotAgent(77) };
                SessionConfig Cfg(ISaveStore store) => new SessionConfig
                {
                    VirtualAgent = new PrincipleAgent(), SeatAgents = agents, Store = store, Undo = UndoMode.Free, ContentHash = "test-content", SaveId = "m10-undo",
                };
                var s = LocalSession.Start(ctx, new RuleOptions(), 2, 77, Campaign.HeartOfSinnoh, Cfg(new SaveStore(dir)), 0);
                long now = 0;
                void Run(int until)
                {
                    while (s.CommandCount < until && !s.GameOver)
                    {
                        now = Math.Max(now, s.NextDueMs);
                        s.Tick(now);
                    }
                }
                Run(300);
                PlayRecord.MarkUploaded(dir, s.CommandCount);
                var points = s.UndoPoints();
                var p = points[points.Count / 2];
                Assert.IsTrue(s.Rewind(p.To, 0, now));
                var rec = SaveStore.Load(dir);
                Assert.IsEmpty(rec.Warnings);
                Assert.AreEqual(p.To, rec.Snapshot.CommandCount, "되돌린 자리의 스냅샷이 가장 새것");
                Assert.IsEmpty(rec.Commands, "로그는 그 자리에서 잘렸다");
                Assert.AreEqual(p.To, rec.Head.Summary.CommandCount, "저장 목록 요약도 되돌린 자리");
                Assert.AreNotEqual("uploaded", PlayRecord.Pack(dir, "1").Skip, "버린 갈래까지 올렸다 — 다음 올리기가 이어진 갈래로 덮어쓴다");
                Assert.AreEqual(s.Hash, LocalSession.Resume(ctx, rec, Cfg(null), 0).Hash, "이어 받은 판 = 되돌린 판");
                // 되돌린 뒤 이어 간 판에서 한 수 무르기 → 다시 이어 받기
                Run(p.To + 80);
                UndoOffer o = null;
                for (int i = 0; i < 400 && !s.GameOver; i++)
                {
                    int a = s.HostCopy().ActiveSeat;
                    if (a >= 0 && (o = s.OfferFor(a)).StepTo >= 0 && s.Undo(a, o.StepTo, now)) break;
                    o = null;
                    now = Math.Max(now, s.NextDueMs);
                    s.Tick(now);
                }
                Assert.IsNotNull(o, "무를 자리가 없었다");
                var again = SaveStore.Load(dir);
                Assert.IsEmpty(again.Warnings);
                Assert.AreEqual(s.CommandCount, again.Snapshot.CommandCount + again.Commands.Count);
                Assert.AreEqual(s.Hash, LocalSession.Resume(ctx, again, Cfg(null), 0).Hash, "한 수 무른 저장 = 무른 판");
            }
            finally
            {
                Clean(dir);
            }
        }

        [Test]
        public void M10_무르기_되돌린스냅샷쓰기가실패하면원래판이열린다()
        {
            // 검토 반영(undo.md §1-5 ③): 되돌린 자리의 스냅샷을 다 쓴 뒤에야 그보다 뒤의 스냅샷을 지운다 — 쓰기가 실패하면 옛 스냅샷 + 로그로 되돌림 전 판이 열린다
            string dir = TempDir();
            try
            {
                var agents = new IAgent[] { new Playout.BotAgent(78), new Playout.BotAgent(78) };
                SessionConfig Cfg(ISaveStore store) => new SessionConfig
                {
                    VirtualAgent = new PrincipleAgent(), SeatAgents = agents, Store = store, Undo = UndoMode.Free, ContentHash = "test-content", SaveId = "m10-undo",
                };
                var s = LocalSession.Start(ctx, new RuleOptions(), 2, 78, Campaign.HeartOfSinnoh, Cfg(new SaveStore(dir)), 0);
                long now = 0;
                while (s.CommandCount < 300 && !s.GameOver)
                {
                    now = Math.Max(now, s.NextDueMs);
                    s.Tick(now);
                }
                var points = s.UndoPoints();
                var p = points[points.Count - 1]; // 가장 오래된 턴 시작 — 남은 스냅샷(최근 2개)은 모두 그보다 뒤
                Assert.IsTrue(SnapNumbers(dir).TrueForAll(n => n > p.To), "뒤 스냅샷만 있다");
                ulong h = s.Hash;
                int count = s.CommandCount;
                string blocker = Path.Combine(dir, "snap-" + p.To + ".json.tmp");
                Directory.CreateDirectory(blocker); // 임시 파일 자리를 폴더로 막는다 — 새 스냅샷 쓰기가 실패한다
                Assert.Catch<Exception>(() => s.Rewind(p.To, 0, now));
                Directory.Delete(blocker);
                Assert.AreEqual(h, s.Hash, "메모리는 그대로");
                Assert.AreEqual(count, s.CommandCount);
                Assert.IsNotEmpty(SnapNumbers(dir), "뒤 스냅샷을 먼저 지우지 않았다");
                var rec = SaveStore.Load(dir);
                Assert.AreEqual(count, rec.Snapshot.CommandCount + rec.Commands.Count, "로그도 그대로");
                Assert.AreEqual(h, LocalSession.Resume(ctx, rec, Cfg(null), 0).Hash, "옛 스냅샷 + 로그 = 되돌림 전 판");
            }
            finally
            {
                Clean(dir);
            }
        }
    }
}
