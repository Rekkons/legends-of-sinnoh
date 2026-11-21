using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan §4 「들어가고 나오는 연출」(조우·배틀 4단계, 04 §8-8 20·21·22·53·68·70·94행)의 순수 부분: 화면을 여는 때 · 카드 뒤집기 · 전환 줄무늬 · 인트로가 드러나는 때 ·
    /// 포획 시간표(결정 54 ①) · 주인공 뒷모습 던지기(결정 53 ④·55 ①) · 결과 줄 · 턴 결과 요약에 적는 변화.
    /// 찾아 둔 판(<see cref="SoloTrail"/>)을 좌석 손님으로 돌려 화면과 같은 길(짜기 → 그리는 단계만 남기기)로 본다.
    /// </summary>
    public class EnterStagingTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        // 화면(GameFlow.Built)과 같은 규칙: 걷기 · 턴 넘김 · 배틀 행 · 들어가고 나오는 행, 그릴 것 없는 단계는 뺀다
        internal static bool Built(FxStep s) =>
            (s.Row == 110 || s.Row == FxRows.TurnSummary || s.Row == FxRows.Handoff || s.Row == FxRows.MyTurn || BattleRows.Built(s.Row) || EnterRows.Built(s.Row)) && BattleRows.Draws(s) && EnterRows.Draws(s);

        sealed class Run
        {
            public readonly List<List<FxEntry>> Updates = new List<List<FxEntry>>();
            public readonly List<GameEvent> Events = new List<GameEvent>();
            public BattleFeed Feed;

            public FxEntry Entry<T>(int nth = 0) where T : GameEvent
            {
                foreach (var update in Updates)
                    foreach (var entry in update)
                        foreach (var e in entry.Events)
                            if (e is T && nth-- == 0) return entry;
                return null;
            }

            public T Event<T>(int nth = 0) where T : GameEvent
            {
                foreach (var e in Events) if (e is T t && nth-- == 0) return t;
                return null;
            }
        }

        // 판을 길잡이로 돈다(보드에 선 뒤의 갱신만 화면 대기열 항목으로 모은다). until = 그만 돌 때
        Run Play(SoloTrail trail, Func<Run, bool> until)
        {
            var run = new Run();
            var pilot = trail.Pilot();
            var session = SoloGame.Start(ctx, new SoloSettings(), trail.Seed, SoloTrail.AiSeed, new PrincipleAgent(), 0, prepare: trail.Prepared ? trail.Prepare : (Action<GameState>)null);
            var client = new SeatClient(session.Link, ctx, SoloGame.Seat);
            var planner = new FxPlanner(SoloGame.Seat, SoloGame.SeatCount);
            client.Updated += u =>
            {
                pilot.Saw(u.Events);
                var entries = planner.Plan(u.Events);
                if (u.View.Phase == Phase.Setup) return;
                FxBuilt.KeepSteps(entries, Built);
                run.Updates.Add(entries);
                run.Events.AddRange(u.Events);
                // 화면(GameFlow.OnUpdated)과 같은 길: 배틀이 시작된 갱신부터 기록 줄을 모은다
                if (run.Feed == null)
                    foreach (var e in u.Events)
                        if (e is BattleStarted) run.Feed = new BattleFeed(ctx, SoloGame.Seat);
                if (run.Feed != null)
                {
                    run.Feed.Feed(u.View, u.Events);
                    run.Feed.Order(entries);
                }
            };
            long now = 0;
            for (int i = 0, answers = 0; i < 20000 && answers < 400 && !until(run); i++)
            {
                var c = client.FirstChoice();
                if (c == null)
                {
                    now += 60000;
                    session.Tick(now);
                    continue;
                }
                var legal = client.Legal(c);
                var a = c.Kind == ChoiceKind.VillagePick ? legal[trail.Village] : c.Kind == ChoiceKind.StarterPick ? legal[trail.Starter] : c.Kind == ChoiceKind.TrainerCardPick ? legal[0] : pilot.Decide(ctx, client.View, c, legal);
                Assert.IsNotNull(a, trail.Name + ": 길잡이가 받을 줄 모르는 선택 " + c.Kind);
                Assert.IsTrue(client.Answer(c, a, now).Accepted, trail.Name + ": " + c.Kind);
                answers++;
            }
            client.Close();
            Assert.IsTrue(until(run), trail.Name + ": 그 장면까지 가지 못했다");
            return run;
        }

        [Test]
        public void M6_간부태홍앞모습은명단의그인물이다()
        {
            // 결정 80 L4 나: 간부 셋 · 태홍은 그 인물의 앞모습(Pt 원작 자산), 조무래기 · 모름은 칸으로 고른 조무래기(결정 70 ⑩ 그대로)
            Assert.AreEqual(FoeArt.Mars, FoeArt.For(GalacticTrainer.Mars, 3));
            Assert.AreEqual(FoeArt.Jupiter, FoeArt.For(GalacticTrainer.Jupiter, 3));
            Assert.AreEqual(FoeArt.Saturn, FoeArt.For(GalacticTrainer.Saturn, 3));
            Assert.AreEqual(FoeArt.Cyrus, FoeArt.For(GalacticTrainer.Cyrus, 3));
            foreach (var t in new[] { GalacticTrainer.None, GalacticTrainer.MaleGrunt, GalacticTrainer.FemaleGrunt })
                for (int cell = -1; cell < 40; cell++) Assert.AreEqual(FoeArt.Grunt(cell), FoeArt.For(t, cell), t + " " + cell);

            // 배틀 기록: 명단 공개(GalacticBattle)의 첫 카드가 그 인물 — 간부 덱 · 보스 덱의 카드마다
            foreach (var who in new[] { GalacticTrainer.Mars, GalacticTrainer.Jupiter, GalacticTrainer.Saturn, GalacticTrainer.Cyrus })
            {
                int card = -1;
                for (int i = 0; i < ctx.Cards.Pokemon.Count && card < 0; i++) if (ctx.Cards.Pokemon[i].Trainer == who) card = i;
                Assert.That(card, Is.GreaterThanOrEqualTo(0), who + " 카드");
                var feed = new BattleFeed(ctx, SoloGame.Seat);
                Assert.AreEqual(GalacticTrainer.None, feed.Foe, "배틀 전");
                var leader = who == GalacticTrainer.Cyrus ? GalacticLeader.Cyrus : GalacticLeader.Commander;
                feed.Feed(null, new GameEvent[] { new BattleStarted { BattleId = 1, Kind = BattleKind.Galactic, Cell = 5 }, new GalacticBattle { Seat = SoloGame.Seat, Leader = leader, LineupCount = 1, Lineup = new[] { card } } });
                Assert.AreEqual(who, feed.Foe, who.ToString());
            }
        }

        [Test]
        public void M4_조무래기그림은칸으로고르고마을마다남녀가섞인다()
        {
            // 결정 70 ⑩: 조무래기는 마을 칸에 선다 — 마을 칸들에 남·여가 다 나오고, 같은 칸은 늘 같은 그림(보기 값 — 칸 번호에서만 셈한다)
            var cells = new List<int>();
            foreach (var f in ctx.Fixtures.Fixtures) if (f.Kind == FixtureKind.Village) cells.Add(f.Cell);
            int women = 0;
            foreach (int c in cells)
            {
                Assert.AreEqual(FoeArt.Female(c), FoeArt.Female(c), "같은 칸은 같은 그림");
                Assert.AreEqual(FoeArt.Female(c) ? FoeArt.GruntFemale : FoeArt.GruntMale, FoeArt.Grunt(c));
                if (FoeArt.Female(c)) women++;
            }
            TestContext.WriteLine("마을 칸 → 여: " + string.Join(" ", cells.ConvertAll(c => c + (FoeArt.Female(c) ? "f" : "m"))));
            Assert.That(women, Is.GreaterThan(0).And.LessThan(cells.Count), "마을 " + cells.Count + "곳에 남·여가 섞인다");
            Assert.AreEqual(FoeArt.GruntMale, FoeArt.Grunt(-1), "칸을 모르면 남자 그림");
            Assert.AreNotEqual(FoeArt.GruntMale, FoeArt.GruntFemale);

            // 배틀 기록이 배틀 칸을 싣는다(화면이 그 칸으로 고른다)
            var feed = new BattleFeed(ctx, SoloGame.Seat);
            Assert.AreEqual(-1, feed.Cell, "배틀 전");
            feed.Feed(null, new GameEvent[] { new BattleStarted { BattleId = 1, Kind = BattleKind.Galactic, Cell = cells[0] } });
            Assert.AreEqual(cells[0], feed.Cell);

            // 찾아 둔 판 둘의 조무래기: Defeat = 남 · Coral = 여 — 연출 캡처(battle-galactic · battle-galactic-f)가 한 장씩 찍는다
            int Grunt(SoloTrail trail)
            {
                BattleStarted Found(Run r)
                {
                    foreach (var e in r.Events) if (e is BattleStarted b && b.Kind == BattleKind.Galactic) return b;
                    return null;
                }
                var bs = Found(Play(trail, r => Found(r) != null));
                Assert.IsNotNull(bs, trail.Name + ": 조무래기 배틀");
                return bs.Cell;
            }
            int defeat = Grunt(SoloTrail.Defeat), coral = Grunt(SoloTrail.Coral);
            TestContext.WriteLine("Defeat " + defeat + " · Coral " + coral);
            Assert.IsFalse(FoeArt.Female(defeat), "Defeat 판의 조무래기 = 남");
            Assert.IsTrue(FoeArt.Female(coral), "Coral 판의 조무래기 = 여");
        }

        [Test]
        public void M4_조우창은카드공개가시작할때배틀화면은전환의가운데에열린다()
        {
            // 04 §8-8 20·21·22행: 찾아보기 1750 → 카드 공개 2500(창이 이때 열린다) / 고른 뒤 600 → 조우 전환 2000(가운데에서 화면이 바뀐다) → 인트로
            var run = Play(SoloTrail.Catch, r => r.Event<BattleStarted>() != null);
            var search = run.Entry<Searched>();
            Assert.AreEqual(PlayTiming.SearchMs.Value, search.Step(EnterRows.Search).Ms, "찾아보기 = 1750(결정 53 ①)");
            Assert.AreEqual(1750, search.Step(EnterRows.Reveal).StartMs);
            Assert.AreEqual(2500, search.Step(EnterRows.Reveal).Ms);
            var seq = new FxSequencer();
            Assert.IsTrue(EnterRows.Reached(seq, EnterRows.Reveal), "대기열에 그 단계가 없으면 이미 지난 것이다");
            seq.Enqueue(new[] { search });
            Assert.IsFalse(EnterRows.Reached(seq, EnterRows.Reveal), "기다리는 항목: 아직");
            seq.Advance(1749);
            Assert.IsFalse(EnterRows.Reached(seq, EnterRows.Reveal), "찾아보기(「!」)가 도는 동안에는 조우 창이 없다");
            seq.Advance(1);
            Assert.IsTrue(EnterRows.Reached(seq, EnterRows.Reveal), "공개 단계가 시작하면 조우 창이 열린다");

            // 카드: 창이 뜨고 300 뒤부터 왼쪽에서 250씩 뒤집힌다(가로 1 → 0 → 1, 절반에서 앞면)
            Assert.IsFalse(EnterRows.FlipFace(0, 300, out float sx));
            Assert.AreEqual(1f, sx);
            Assert.IsFalse(EnterRows.FlipFace(0, 300 + 62, out sx));
            Assert.Less(sx, 1f);
            Assert.IsTrue(EnterRows.FlipFace(0, 300 + 125, out sx), "절반에서 앞면으로 바뀐다");
            Assert.AreEqual(0f, sx, 0.01f);
            Assert.IsTrue(EnterRows.FlipFace(0, 300 + 250, out sx));
            Assert.AreEqual(1f, sx);
            Assert.IsFalse(EnterRows.FlipFace(1, 300 + 250, out _), "둘째 장은 250 뒤에 시작한다");
            Assert.IsTrue(EnterRows.FlipFace(2, 300 + 750, out _), "셋째 장까지 1050에 다 뒤집힌다(공개 단계 2500 안)");
            Assert.IsTrue(EnterRows.FlipFace(2, int.MaxValue, out sx), "단계가 지난 뒤에는 모두 앞면");
            Assert.AreEqual(1f, sx);
            Assert.IsTrue(EnterRows.PulseOn(0));
            Assert.IsFalse(EnterRows.PulseOn(250), "맥박: 500마다 앞 절반만 켜진다");
            Assert.IsTrue(EnterRows.PulseOn(1100));
            Assert.IsFalse(EnterRows.PulseOn(1500), "세 번 뒤에는 꺼져 있다");

            var enter = run.Entry<BattleStarted>();
            var picked = enter.Step(EnterRows.Picked);
            var wipe = enter.Step(EnterRows.Wipe);
            Assert.AreEqual(600, picked.Ms, "고른 뒤 600");
            Assert.AreEqual(FxLayer.Block, wipe.Layer, "조우 전환은 당사자 차단 컷신");
            Assert.AreEqual(2000, wipe.Ms);
            seq = new FxSequencer();
            seq.Enqueue(new[] { enter });
            seq.Advance(wipe.StartMs + 999);
            Assert.IsFalse(EnterRows.Reached(seq, EnterRows.Wipe, EnterBeats.WipeSwapAt), "줄무늬가 다 닫히기 전에는 조우 창이 남아 있다");
            Assert.AreEqual(0f, EnterRows.WipeCover(wipe, wipe.StartMs + 599), "예비(카메라가 다가간다) 동안은 줄무늬가 없다");
            Assert.AreEqual(0.5f, EnterRows.WipeCover(wipe, wipe.StartMs + 800), 0.01f, "닫히는 중");
            seq.Advance(1);
            Assert.IsTrue(EnterRows.Reached(seq, EnterRows.Wipe, EnterBeats.WipeSwapAt), "전환의 가운데에서 배틀 화면이 열린다");
            Assert.AreEqual(1f, EnterRows.WipeCover(wipe, wipe.StartMs + 1000), "화면이 바뀔 때 가운데 필드는 다 덮여 있다");
            Assert.AreEqual(1f, EnterRows.WipeCover(wipe, wipe.StartMs + 1199));
            Assert.AreEqual(0.5f, EnterRows.WipeCover(wipe, wipe.StartMs + 1400), 0.01f, "열리는 중");
            Assert.AreEqual(0f, EnterRows.WipeCover(wipe, wipe.StartMs + 1600), "다 열렸다");
            Assert.AreEqual(0f, EnterRows.WipeCover(wipe, wipe.EndMs), "단계 밖");

            // 인트로(53행): 야생이 먼저 나오고(+300) 내 포켓몬이 그 뒤(+1750) — 이벤트는 내 쪽이 먼저 적힌다
            var intro = enter.Step(FxRows.Intro);
            Assert.AreEqual(3500, intro.Ms);
            SentIn mine = null, wild = null;
            foreach (var e in intro.Events)
                if (e is SentIn s)
                {
                    if (mine == null) mine = s;
                    else wild = s;
                }
            Assert.IsNotNull(wild, "야생 배틀을 연 항목의 인트로에 양쪽 출전이 함께 있다");
            Assert.AreEqual(intro.StartMs + EnterBeats.IntroLeadMs, FxReveal.At(enter, wild));
            Assert.AreEqual(intro.StartMs + EnterBeats.IntroSecondMs, FxReveal.At(enter, mine));
            Assert.Less(FxReveal.At(enter, wild), FxReveal.At(enter, mine), "야생 포켓몬 → 내 포켓몬");
            Assert.AreEqual(wipe.StartMs, FxReveal.At(enter, run.Event<BattleStarted>()), "배틀 시작(「…승부를 걸어왔다!」 줄)은 전환과 함께 드러난다");

            // 인트로의 움직임: 드러난 순간 화면 밖 쪽에서 미끄러져 들어오고(상대는 오른쪽, 나는 왼쪽) 체력 상자가 뒤따른다
            seq = new FxSequencer();
            seq.Enqueue(new[] { enter });
            seq.Advance(FxReveal.At(enter, wild));
            var pose = BattlePose.Of(seq, wild.PokemonId, wild.Side, true);
            Assert.AreEqual(EnterBeats.IntroSlidePx, pose.Dx, "상대는 오른쪽에서");
            Assert.IsTrue(pose.BoxHidden);
            seq.Advance(EnterBeats.IntroSlideMs);
            pose = BattlePose.Of(seq, wild.PokemonId, wild.Side, true);
            Assert.AreEqual(0, pose.Dx, "제자리");
            Assert.IsFalse(pose.BoxHidden);
            Assert.AreEqual(-EnterBeats.IntroBoxPx, pose.BoxDx, "상대 체력 상자는 왼쪽에서 들어오기 시작한다");
            seq.Advance(EnterBeats.IntroBoxMs);
            Assert.AreEqual(0, BattlePose.Of(seq, wild.PokemonId, wild.Side, true).BoxDx);
            // 내 첫 출전은 주인공이 던진 볼에서 나온다(결정 99 ②) — 미끄러져 들어오지 않고, 체력 상자는 그대로 뒤따른다
            Assert.IsTrue(IntroStage.Thrown(enter, intro, mine), "배틀을 연 항목의 내 출전 = 주인공이 던진다");
            Assert.IsFalse(IntroStage.Thrown(enter, intro, wild), "야생은 던지지 않는다");
            seq.Advance(FxReveal.At(enter, mine) - seq.LocalMs);
            pose = BattlePose.Of(seq, mine.PokemonId, mine.Side, false);
            Assert.AreEqual(0, pose.Dx, "볼에서 나온다 — 옆에서 미끄러져 들어오지 않는다");
            Assert.IsTrue(pose.BoxHidden);
            Assert.AreEqual(0, BattlePose.Of(seq, mine.PokemonId, mine.Side, true).Dx, "위쪽 자리에 그리는 화면(상대가 본 내 포켓몬)도 볼에서 나온다 — 화면 밖에서 날아온 볼(M9 다듬기 P05)");
        }

        [Test]
        public void M65_인트로첫출전은주인공뒷모습이던진볼에서나온다_결정99()
        {
            // 원작 Pt(pret battle_display.c Task_SetTrainerEncounter → Task_ThrowTrainerBall): 뒷모습이 들어와 서 있다가 던지고, 볼이 열리며 포켓몬이 나온 뒤 물러난다
            const int hx = 53, hy = 97, fx = 64, fy = 144, lead = EnterBeats.IntroSecondMs;
            IntroPose At(int u, int l = lead) => IntroStage.Pose(u, l, hx, hy, fx, fy);
            Assert.AreEqual(EnterBeats.IntroLeadMs, EnterBeats.IntroSecondMs - EnterBeats.IntroTrainerLeadMs, "둘이 함께 나오는 단계에서 뒷모습은 상대와 같은 때 들어온다");
            Assert.IsFalse(At(-EnterBeats.IntroTrainerLeadMs - 1).Trainer, "들어오기 전");
            var p = At(-EnterBeats.IntroTrainerLeadMs);
            Assert.IsTrue(p.Trainer);
            Assert.AreEqual(-EnterBeats.TrainerSlidePx, p.TrainerDx, "왼쪽 밖에서 들어온다");
            Assert.Less(p.ThrowMs, 0, "아직 던지지 않는다(서 있는 프레임)");
            p = At(-EnterBeats.IntroTrainerLeadMs + EnterBeats.IntroSlideMs);
            Assert.AreEqual(0, p.TrainerDx, "다 들어와 선다");
            Assert.Less(p.ThrowMs, 0);
            Assert.IsFalse(p.Ball);
            Assert.AreEqual(0, At(-IntroStage.ThrowLeadMs).ThrowMs, "던지기 시작");
            p = At(-EnterBeats.IntroFlyMs);
            Assert.AreEqual(EnterBeats.ReleaseMs, p.ThrowMs, "볼이 손을 떠나는 때 = 던지기의 260ms(포획과 같은 박자)");
            Assert.IsTrue(p.Ball);
            Assert.AreEqual((hx, hy), (p.BallX, p.BallY), "볼은 손 자리에서 나온다");
            Assert.IsFalse(At(-EnterBeats.IntroFlyMs - 1).Ball, "손을 떠나기 전에는 볼이 없다");
            p = At(-1);
            Assert.IsTrue(p.Ball);
            Assert.GreaterOrEqual(p.BallFrame, 0);
            Assert.AreEqual(fx, p.BallX, 2, "내 발판 위로 날아온다");
            p = At(0);
            Assert.AreEqual(-1, p.BallFrame, "드러나는 때 볼이 열린다");
            Assert.AreEqual((fx, fy - EnterBeats.IntroOpenAbovePx), (p.BallX, p.BallY));
            Assert.AreEqual(1f, p.MonWhite, "하얀 채");
            Assert.Less(p.MonScale, 0.1f, "작게 나와");
            p = At(EnterBeats.IntroPopMs / 2);
            Assert.AreEqual(0.5f, p.MonScale, 0.01f, "커진다");
            Assert.AreEqual(0.5f, p.MonWhite, 0.01f);
            p = At(EnterBeats.IntroPopMs);
            Assert.IsFalse(p.Ball, "볼이 사라지고");
            Assert.AreEqual(1f, p.MonScale);
            Assert.AreEqual(0f, p.MonWhite);
            // 뒷모습은 손을 떠난 100 뒤부터 250 동안 왼쪽으로 물러난다
            int leave = -EnterBeats.IntroFlyMs + EnterBeats.IntroLeaveLagMs;
            Assert.AreEqual(0, At(leave).TrainerDx);
            Assert.Less(At(leave + EnterBeats.LeaveMs - 1).TrainerDx, -EnterBeats.TrainerSlidePx / 2);
            Assert.IsFalse(At(leave + EnterBeats.LeaveMs).Trainer, "물러났다");
            Assert.Less(leave + EnterBeats.LeaveMs, EnterBeats.IntroBoxLeadMs, "체력 상자가 들어오기 전에 물러나 있다");
            // 첫 출전을 따로 고른 항목(그 출전만 있는 인트로 — +1100): 단계가 시작할 때 들어온다
            Assert.IsFalse(At(-EnterBeats.IntroPickMs - 1, EnterBeats.IntroPickMs).Trainer, "단계가 시작하기 전에는 없다");
            p = At(-EnterBeats.IntroPickMs, EnterBeats.IntroPickMs);
            Assert.IsTrue(p.Trainer);
            Assert.AreEqual(-EnterBeats.TrainerSlidePx, p.TrainerDx);
            Assert.AreEqual(0, At(-IntroStage.ThrowLeadMs, EnterBeats.IntroPickMs).TrainerDx, "던지기 전에 다 들어와 선다");

            // 누가 던지나: 배틀을 연 항목 · 첫 출전을 고른 항목의 좌석 포켓몬만 — 쓰러진 뒤의 다음 포켓몬 · 야생은 아니다
            FxEntry Entry(ActionKind kind, SentIn si, bool opens)
            {
                var e = new FxEntry { Kind = kind };
                var st = new FxStep { Row = FxRows.Intro, Layer = FxLayer.Item, StartMs = 100, Ms = 3500 };
                st.Events.Add(si);
                e.Steps.Add(st);
                if (opens) e.Events.Add(new BattleStarted());
                e.Events.Add(si);
                return e;
            }
            var mon = new SentIn { Seat = 0, Side = 0, PokemonId = 1 };
            var pick = Entry(ActionKind.SendIn, mon, false);
            Assert.IsTrue(IntroStage.Thrown(pick, pick.Steps[0], mon), "첫 출전을 고른 항목");
            Assert.AreEqual(100 + EnterBeats.IntroPickMs, FxReveal.At(pick, mon), "혼자 나오는 첫 출전은 뒷모습이 던질 틈 뒤에 드러난다");
            var next = Entry(ActionKind.NextPokemon, mon, false);
            Assert.IsFalse(IntroStage.Thrown(next, next.Steps[0], mon), "쓰러진 뒤의 다음 포켓몬에는 주인공이 없다(원작 — 볼만)");
            Assert.AreEqual(100 + EnterBeats.IntroLeadMs, FxReveal.At(next, mon));
            var beast = new SentIn { Seat = SeatId.Wild, Side = 1, PokemonId = 2 };
            var open = Entry(ActionKind.EncounterPick, beast, true);
            Assert.IsFalse(IntroStage.Thrown(open, open.Steps[0], beast), "야생은 풀숲에서");
            var opened = Entry(ActionKind.EncounterPick, mon, true);
            Assert.IsTrue(IntroStage.Thrown(opened, opened.Steps[0], mon), "배틀을 연 항목");
        }

        [Test]
        public void M9_조우전환의무늬는상대에따라다르고_덮음의처음과끝은같다_P14()
        {
            // 갈래: 야생(무리 · 우두머리) / 전설(신 · 시련) / 트레이너(갤럭시단 · 배신자) / 울트라 비스트(네크로즈마)
            Assert.AreEqual(WipeStyle.Wild, WipeBeats.StyleOf(BattleKind.Wild));
            Assert.AreEqual(WipeStyle.Wild, WipeBeats.StyleOf(BattleKind.Swarm));
            Assert.AreEqual(WipeStyle.Wild, WipeBeats.StyleOf(BattleKind.Alpha));
            Assert.AreEqual(WipeStyle.Legend, WipeBeats.StyleOf(BattleKind.Legendary));
            Assert.AreEqual(WipeStyle.Legend, WipeBeats.StyleOf(BattleKind.God));
            Assert.AreEqual(WipeStyle.Legend, WipeBeats.StyleOf(BattleKind.Trial));
            Assert.AreEqual(WipeStyle.Trainer, WipeBeats.StyleOf(BattleKind.Trainer));
            Assert.AreEqual(WipeStyle.Trainer, WipeBeats.StyleOf(BattleKind.Galactic));
            Assert.AreEqual(WipeStyle.Trainer, WipeBeats.StyleOf(BattleKind.Traitor));
            Assert.AreEqual(WipeStyle.Rift, WipeBeats.StyleOf(BattleKind.UltraBeast));
            Assert.AreEqual(WipeStyle.Rift, WipeBeats.StyleOf(BattleKind.Necrozma));
            foreach (BattleKind kind in Enum.GetValues(typeof(BattleKind)))
            {
                var st = new FxStep { Row = EnterRows.Wipe, Ms = 2000 };
                st.Events.Add(new BattleStarted { Kind = kind });
                Assert.AreEqual(WipeBeats.StyleOf(kind), WipeBeats.StyleOf(st), kind + ": 단계가 연 배틀에서 읽는다");
            }
            Assert.AreEqual(WipeStyle.Wild, WipeBeats.StyleOf((FxStep)null), "역전환처럼 단계가 없으면 야생");

            const int bars = 10;
            foreach (WipeStyle style in Enum.GetValues(typeof(WipeStyle)))
            {
                float min = 1f, max = 0f;
                for (int i = 0; i < bars; i++)
                {
                    Assert.AreEqual(0f, WipeBeats.Bar(style, i, bars, 0f), style + ": 덮음 0이면 줄이 없다");
                    Assert.AreEqual(1f, WipeBeats.Bar(style, i, bars, 1f), style + ": 화면이 바뀔 때 가운데 필드는 다 덮여 있다");
                    float d = WipeBeats.Delay(style, i, bars);
                    Assert.That(d, Is.InRange(0f, 1f), style + " 늦음");
                    min = Math.Min(min, d);
                    max = Math.Max(max, d);
                    float before = 0f;
                    for (int c = 0; c <= 100; c++)
                    {
                        float v = WipeBeats.Bar(style, i, bars, c / 100f);
                        Assert.That(v, Is.InRange(0f, 1f).And.GreaterThanOrEqualTo(before), style + ": 덮음이 늘면 줄도 는다(되돌아가지 않는다)");
                        before = v;
                    }
                }
                if (style == WipeStyle.Wild) Assert.AreEqual(0f, max, "야생: 모든 줄이 함께(옛 그대로)");
                else { Assert.AreEqual(0f, min, style + ": 맨 먼저 자라는 줄"); Assert.AreEqual(1f, max, 1e-6f, style + ": 맨 늦게 자라는 줄"); }
            }
            // 야생 = 옛 줄무늬 그대로: 짝수 줄은 왼쪽 · 홀수 줄은 오른쪽에서, 늘인 만큼 = 덮음
            Assert.AreEqual(0, WipeBeats.Origin(WipeStyle.Wild, 0));
            Assert.AreEqual(100, WipeBeats.Origin(WipeStyle.Wild, 1));
            Assert.AreEqual(0.37f, WipeBeats.Bar(WipeStyle.Wild, 3, bars, 0.37f));
            // 전설 = 가운데 두 줄부터 번지는 마름모(위아래가 대칭) · 가운데에서 양쪽으로
            Assert.AreEqual(50, WipeBeats.Origin(WipeStyle.Legend, 0));
            Assert.AreEqual(0f, WipeBeats.Delay(WipeStyle.Legend, 4, bars));
            Assert.AreEqual(0f, WipeBeats.Delay(WipeStyle.Legend, 5, bars));
            for (int i = 0; i < bars / 2; i++) Assert.AreEqual(WipeBeats.Delay(WipeStyle.Legend, i, bars), WipeBeats.Delay(WipeStyle.Legend, bars - 1 - i, bars), 1e-6f);
            Assert.That(WipeBeats.Bar(WipeStyle.Legend, 4, bars, 0.4f), Is.GreaterThan(WipeBeats.Bar(WipeStyle.Legend, 2, bars, 0.4f)).And.GreaterThan(WipeBeats.Bar(WipeStyle.Legend, 0, bars, 0.4f)));
            // 트레이너 = 위에서 아래로 한 줄씩(모두 왼쪽에서)
            for (int i = 1; i < bars; i++)
            {
                Assert.AreEqual(0, WipeBeats.Origin(WipeStyle.Trainer, i));
                Assert.That(WipeBeats.Delay(WipeStyle.Trainer, i, bars), Is.GreaterThan(WipeBeats.Delay(WipeStyle.Trainer, i - 1, bars)));
            }
            Assert.AreEqual(1f, WipeBeats.Bar(WipeStyle.Trainer, 0, bars, 0.5f), "덮음 절반: 첫 줄은 다 닫혔고");
            Assert.AreEqual(0f, WipeBeats.Bar(WipeStyle.Trainer, bars - 1, bars, 0.5f), "마지막 줄은 아직");
            // 울트라 비스트 = 들쭉날쭉(이웃한 줄의 늦음이 크게 다르다) · 줄마다 다른 늦음
            var seen = new HashSet<float>();
            for (int i = 0; i < bars; i++) Assert.IsTrue(seen.Add(WipeBeats.Delay(WipeStyle.Rift, i, bars)), "줄마다 다른 늦음");
            Assert.That(Math.Abs(WipeBeats.Delay(WipeStyle.Rift, 1, bars) - WipeBeats.Delay(WipeStyle.Rift, 0, bars)), Is.GreaterThan(0.25f));

            // 전설의 빛: 줄무늬가 닫히기 앞(단계의 200‰)에 켜져 300‰에 가장 밝고 닫히는 가운데(400‰)에 꺼진다 — 다른 갈래는 없다. 덮음(WipeCover)·화면이 바뀌는 때는 갈래와 무관
            var step = new FxStep { Row = EnterRows.Wipe, StartMs = 600, Ms = 2500 };
            Assert.AreEqual(0f, WipeBeats.Light(WipeStyle.Legend, step, 600 + 499));
            Assert.AreEqual(WipeBeats.LightMax / 100f, WipeBeats.Light(WipeStyle.Legend, step, 600 + 750), 1e-4f);
            Assert.That(WipeBeats.Light(WipeStyle.Legend, step, 600 + 625), Is.InRange(0.3f, 0.4f));
            Assert.AreEqual(0f, WipeBeats.Light(WipeStyle.Legend, step, 600 + 1000));
            Assert.AreEqual(0f, WipeBeats.Light(WipeStyle.Legend, step, 599), "단계 밖");
            foreach (var style in new[] { WipeStyle.Wild, WipeStyle.Trainer, WipeStyle.Rift }) Assert.AreEqual(0f, WipeBeats.Light(style, step, 600 + 750));
            Assert.That(WipeBeats.LightTo, Is.LessThanOrEqualTo(EnterBeats.WipeSwapAt), "빛은 화면이 바뀌기 전에 꺼진다");
        }

        [Test]
        public void M9_상대트레이너의첫출전은화면밖에서날아온볼에서나온다_P05()
        {
            FxEntry Entry(ActionKind kind, SentIn si, bool opens)
            {
                var e = new FxEntry { Kind = kind };
                var st = new FxStep { Row = FxRows.Intro, Layer = FxLayer.Item, StartMs = 100, Ms = 3500 };
                st.Events.Add(si);
                e.Steps.Add(st);
                if (opens) e.Events.Add(new GalacticBattle());
                e.Events.Add(si);
                return e;
            }
            // 누구의 볼인가: 갤럭시단 · 좌석(도전 · 트레이너전)의 첫 출전만 — 야생 · 울트라 비스트 · 쓰러진 뒤의 다음 포켓몬은 아니다
            var grunt = new SentIn { Seat = SeatId.Galactic, Side = 1, PokemonId = 7 };
            var open = Entry(ActionKind.EncounterPick, grunt, true);
            Assert.IsTrue(IntroStage.FoeThrown(open, open.Steps[0], grunt), "갤럭시단 배틀을 연 항목");
            var next = Entry(ActionKind.NextPokemon, grunt, false);
            Assert.IsFalse(IntroStage.FoeThrown(next, next.Steps[0], grunt), "다음 포켓몬은 인트로의 볼이 아니다");
            var rival = new SentIn { Seat = 1, Side = 1, PokemonId = 8 };
            var pick = Entry(ActionKind.SendIn, rival, false);
            Assert.IsTrue(IntroStage.FoeThrown(pick, pick.Steps[0], rival), "도전 상대가 고른 첫 출전");
            foreach (int seat in new[] { SeatId.Wild, SeatId.UltraBeast })
            {
                var wild = new SentIn { Seat = seat, Side = 1, PokemonId = 9 };
                var w = Entry(ActionKind.EncounterPick, wild, true);
                Assert.IsFalse(IntroStage.FoeThrown(w, w.Steps[0], wild), "야생 · 울트라 비스트는 볼에서 나오지 않는다");
            }

            Assert.AreEqual(100 + EnterBeats.IntroLeadMs, FxReveal.At(open, grunt), "드러나는 때는 그대로(방장 길이 변화 없음)");

            // 볼: 단계 시작에 오른쪽 화면 밖에서 떠나 포물선으로 → 드러나는 때 상대 발 위에서 열리고 포켓몬이 하얀 채 커진다(내 쪽 볼 열림과 같은 꼴)
            const int fx = 192, fy = 88, lead = EnterBeats.IntroLeadMs;
            Assert.IsFalse(IntroStage.FoePose(-lead - 1, lead, fx, fy).Ball, "단계가 시작하기 전에는 없다");
            var p = IntroStage.FoePose(-EnterBeats.IntroFoeFlyMs, lead, fx, fy);
            Assert.IsTrue(p.Ball);
            Assert.AreEqual((EnterBeats.IntroFoeFromX, fy - EnterBeats.IntroFoeFromAbovePx), (p.BallX, p.BallY), "화면 오른쪽 밖에서");
            Assert.GreaterOrEqual(p.BallX - 8, 256, "볼 그림(16px)이 화면 밖");
            p = IntroStage.FoePose(-EnterBeats.IntroFoeFlyMs / 2, lead, fx, fy);
            Assert.Less(p.BallY, fy - EnterBeats.IntroFoeFromAbovePx, "포물선의 꼭대기");
            Assert.GreaterOrEqual(p.BallFrame, 0, "도는 프레임");
            p = IntroStage.FoePose(0, lead, fx, fy);
            Assert.AreEqual(-1, p.BallFrame, "열린 볼");
            Assert.AreEqual((fx, fy - EnterBeats.OpenAbovePx), (p.BallX, p.BallY));
            Assert.AreEqual(1f, p.MonWhite);
            Assert.Less(p.MonScale, 0.1f);
            p = IntroStage.FoePose(EnterBeats.IntroPopMs, lead, fx, fy);
            Assert.IsFalse(p.Ball);
            Assert.AreEqual(1f, p.MonScale);
            Assert.AreEqual(0f, p.MonWhite);
            // 드러나기까지가 짧으면(없는 틈) 그만큼만 난다
            Assert.IsFalse(IntroStage.FoePose(-101, 100, fx, fy).Ball);
            Assert.IsTrue(IntroStage.FoePose(-100, 100, fx, fy).Ball);
        }

        [Test]
        public void M4_포획시간표는결정54의표와같다()
        {
            // 결정 54 ①(목업 r5-fx README) · 결정 126 ②(던지기 1.5배 — 흔들림 시작 1650 → 2480): 판정 = max(2350, 2480 + 470 × 흔들림 수), 전체 = 판정 + 성공 900 · 실패 750
            var cases = new[]
            {
                // 성공, 흔들림, 판정, 전체, 흔들림 시작, 결말 대사, 굴림이 멈춘 때, 세기 간격, 첫 세기
                (true, 3, 3890, 4790, 2480, 4240, 1251, 250, 3040),
                (false, 2, 3420, 4170, 2480, 3670, 1401, 250, 2570),
                (false, 1, 2950, 3700, 2480, 3200, 1517, 250, 2100),
                (false, 0, 2480, 3230, 2480, 2730, 1667, 180, 1840),
            };
            const AttackDieFace R = AttackDieFace.RegularHit;
            foreach (var (ok, shakes, verdict, total, shakeAt, line, roll, every, first) in cases)
            {
                var c = new CaptureAttempt { Success = ok, Shakes = shakes, Faces = new[] { R, R, R, R } };
                string name = (ok ? "성공" : "실패") + " 흔들림 " + shakes;
                Assert.AreEqual(verdict, CaptureStage.VerdictMs(c), name + " 판정");
                Assert.AreEqual(verdict, PlayTiming.CaptureVerdictMs(shakes), name);
                Assert.AreEqual(total, PlayTiming.CaptureMs(ok, shakes), name + " 전체");
                Assert.AreEqual(total, PlayTiming.RowMs(c, CaptureStage.Row), name + " 68행 길이");
                Assert.AreEqual(shakeAt, CaptureStage.ShakeStartMs(c), name + " 흔들림 시작");
                Assert.AreEqual(line, CaptureStage.LineMs(c), name + " 결말 대사");
                // 세기(주사위 4개 — 목업의 장면): 마지막 주사위(합계)가 판정 100ms 전
                Assert.AreEqual(every, CaptureStage.CountEveryMs(verdict, roll, 4), name + " 세기 간격");
                Assert.AreEqual(first, CaptureStage.CountAtMs(verdict, every, 4, 0), name + " 첫 세기");
                Assert.AreEqual(verdict - EnterBeats.SumLeadMs, CaptureStage.CountAtMs(verdict, every, 4, 3), name + " 합계는 판정 100ms 전");
                Assert.GreaterOrEqual(first, roll + EnterBeats.RollGapMs, name + " 굴림이 멈춘 뒤에 센다");
                Assert.AreEqual(0, CaptureStage.Counted(verdict, every, 4, first - 1));
                Assert.AreEqual(4, CaptureStage.Counted(verdict, every, 4, verdict - EnterBeats.SumLeadMs));

                // 모습(DS px — 손 (50, 90) → 상대 발 (192, 92)): 뒷모습이 들어와 던지고, 볼이 날아가 열리고, 떨어져 튕기고, 흔들리고, 판정
                CapturePose At(int t) => CaptureStage.Pose(c, t, total, 50, 90, 192, 92);
                Assert.IsTrue(At(0).Trainer);
                Assert.AreEqual(-EnterBeats.TrainerSlidePx, At(0).TrainerDx, "왼쪽 밖에서");
                Assert.AreEqual(0, At(EnterBeats.TrainerInMs).TrainerDx, "180에 제자리");
                Assert.IsFalse(At(EnterBeats.ReleaseMs - 1).Ball, "손을 떠나기 전");
                var fly = At(EnterBeats.ReleaseMs);
                Assert.IsTrue(fly.Ball);
                Assert.AreEqual((50, 90), (fly.BallX, fly.BallY), "손에서");
                Assert.IsTrue(At(599).Mon && At(599).MonWhite == 0f, "날아가는 동안 포켓몬은 그대로");
                var open = At(EnterBeats.FlyEndMs);
                Assert.AreEqual(-1, open.BallFrame, "열린 볼");
                Assert.AreEqual((192, 92 - EnterBeats.OpenAbovePx), (open.BallX, open.BallY));
                Assert.AreEqual(1f, open.MonWhite, "포켓몬만 하얗게(화면 플래시 없음 — 결정 53 ②)");
                Assert.IsFalse(At(EnterBeats.SuckEndMs).Mon, "빨려 든 뒤에는 볼만 남는다");
                Assert.AreEqual(92 - EnterBeats.RestAbovePx, At(EnterBeats.BounceEndMs).BallY, "튕긴 뒤 발판 위에");
                Assert.AreEqual(192, At(shakeAt - 1).BallX, "흔들리기 전에는 가만히");
                if (shakes > 0) Assert.AreEqual(192 - EnterBeats.ShakePx, At(shakeAt).BallX, name + " 흔들림 시작");
                Assert.IsFalse(At(verdict - 1).BallDark);
                Assert.IsFalse(At(verdict - 1).Mon);
                if (ok)
                {
                    Assert.IsTrue(At(verdict).BallDark, "딸깍: 볼이 어두워진다");
                    Assert.AreEqual(1, At(verdict).Stars);
                    Assert.AreEqual(3, At(verdict + 2 * EnterBeats.StarEveryMs).Stars, "별 셋");
                    var end = At(int.MaxValue / 2);
                    Assert.IsTrue(end.Trainer && end.TrainerDx == 0, "성공하면 뒷모습이 남는다(결정 54 ②)");
                    Assert.IsTrue(end.Ball && end.BallDark && !end.Mon);
                }
                else
                {
                    Assert.AreEqual(-1, At(verdict).BallFrame, "튀어나옴: 볼이 열린다");
                    Assert.IsTrue(At(verdict + EnterBeats.BreakMs).Mon && At(verdict + EnterBeats.BreakMs).MonScale == 1f, "포켓몬이 돌아온다");
                    Assert.IsFalse(At(verdict + EnterBeats.BreakMs).Ball);
                    Assert.AreEqual(0, At(total - EnterBeats.LeaveMs).TrainerDx, "끝 250ms 전까지는 서 있다");
                    Assert.Less(At(total - EnterBeats.LeaveMs / 2).TrainerDx, 0, "물러나는 중");
                    Assert.IsFalse(At(total).Trainer, "실패하면 물러난다(결정 54 ②)");
                    Assert.IsFalse(At(int.MaxValue / 2).Trainer || At(int.MaxValue / 2).Ball);
                }
            }

            // 상태 이상인 대상(A7): 첫 심볼 하나가 일반으로 — 결과 줄의 면과 바뀐 주사위
            const AttackDieFace S = AttackDieFace.StatusSymbol, C = AttackDieFace.CriticalHit;
            var turned = new CaptureAttempt { Faces = new[] { R, S, S, C }, StatusConverted = true };
            Assert.AreEqual(1, CaptureStage.Converted(turned));
            CollectionAssert.AreEqual(new[] { R, R, S, C }, CaptureStage.FinalFaces(turned));
            Assert.AreEqual(-1, CaptureStage.Converted(new CaptureAttempt { Faces = new[] { R, S } }));

            // 결과 창(70행): 줄이 500 뒤부터 500마다 하나씩
            Assert.AreEqual(0, ResultStage.Landed(3, 499));
            Assert.AreEqual(1, ResultStage.Landed(3, 500));
            Assert.AreEqual(3, ResultStage.Landed(3, 1500));
        }

        [Test]
        public void M4_찾아둔판의첫볼은성공과실패셋의박자로재생된다_결정54()
        {
            var cases = new[] { (SoloTrail.Catch, true, 3, 4790), (SoloTrail.Miss2, false, 2, 4170), (SoloTrail.Miss1, false, 1, 3700), (SoloTrail.Miss0, false, 0, 3230) };
            foreach (var (trail, ok, shakes, total) in cases)
            {
                var run = Play(trail, r => r.Event<CaptureAttempt>() != null);
                var c = run.Event<CaptureAttempt>();
                Assert.AreEqual((ok, shakes), (c.Success, c.Shakes), trail.Name + ": 찾아 둔 판이 달라졌다 — 마을 0 · 스타팅 0 · 볼 9에서 첫 볼이 그렇게 되는 시드를 다시 찾는다");
                Assert.AreEqual(3, c.Faces.Length, "몬스터볼 = 포획 주사위 3개");
                var entry = run.Entry<CaptureAttempt>();
                var step = CaptureStage.StepOf(entry);
                Assert.IsNotNull(step, trail.Name);
                Assert.AreEqual(0, step.StartMs);
                Assert.AreEqual(total, step.Ms, trail.Name + " 포획 단계 길이");
                Assert.AreEqual(CaptureStage.VerdictMs(c), FxReveal.At(entry, c), "성공·실패는 판정 때 드러난다(계산 줄 · 기록 줄 · 잡힌 포켓몬)");
                Assert.IsNull(BattleRoll.StepOf(entry), "포획 굴림은 따로 56행을 세지 않는다 — 68행 안에 겹친다");
                // 던진 볼은 손을 떠나는 순간 줄어든다(표시 값 단서)
                bool ball = false;
                foreach (var cue in entry.Cues)
                    if (!cue.IsSound && cue.Key.Kind == ShownKind.Res && cue.Key.Res == ResKind.Ball)
                    {
                        ball = true;
                        Assert.AreEqual(EnterBeats.ReleaseMs, cue.AtMs, "볼 재고 단서 = 손을 떠날 때");
                        Assert.AreEqual(cue.Before - 1, cue.After);
                    }
                Assert.IsTrue(ball, "볼 재고 단서");
                if (!ok)
                {
                    Assert.AreEqual(1, entry.Steps.Count, "실패한 포획의 항목 = 포획 단계 하나(상대의 공격은 다음 항목)");
                    continue;
                }
                // 성공: 포획 → 잡은 뒤(결말 대사가 머문다) → 결과(받은 것) — 배틀 끝은 결과 단계가 시작할 때 드러난다
                var rows = new List<int>();
                foreach (var s in entry.Steps) rows.Add(s.Row);
                CollectionAssert.AreEqual(new[] { 680, 690, 700 }, rows);
                var ended = run.Event<BattleEnded>();
                Assert.AreEqual(entry.Step(700).StartMs, FxReveal.At(entry, ended));
                Assert.AreEqual(Math.Min(6000, 3000 + 500 * ended.Changes.Length), entry.Step(700).Ms, "결과 = 3000 + 500 × 받은 것");
                // 턴 결과 요약: 잡은 턴에는 변화가 있어 한 좌석 판에서도 요약 단계가 선다(결정 57 ③)
                Assert.IsTrue(PlayTiming.IsTurnGain(run.Event<PokemonGained>()) && FxRows.TurnGain(run.Event<PokemonGained>()));
                Assert.IsTrue(PlayTiming.IsTurnGain(ended) && FxRows.TurnGain(ended));
                var next = run.Entry<TurnStarted>(1);
                Assert.IsNotNull(next, "포획으로 턴이 끝나 다음 턴이 시작된다");
                Assert.IsNotNull(next.Step(FxRows.TurnSummary), "요약 단계");
                Assert.AreEqual(PlayTiming.TurnSummaryMs.Value, next.Step(FxRows.TurnSummary).Ms);
            }
        }

        [Test]
        public void M4_배틀기록줄은화면에드러난순서대로쌓인다()
        {
            // 인트로(53행)는 야생이 먼저(+300), 내 포켓몬이 그 뒤(+1750)에 드러나지만 이벤트는 내 쪽이 먼저 온다 — 기록 기둥에서 「가랏!」이 「앗! 야생 …」 위로 끼어들면 안 된다
            var run = Play(SoloTrail.Catch, r => r.Event<BattleEnded>() != null);
            var lines = run.Feed.Lines;
            int wild = -1, mine = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Key == BattleFeed.K + "send.wild" && wild < 0) wild = i;
                if (lines[i].Key == BattleFeed.K + "send.mine" && mine < 0) mine = i;
            }
            Assert.GreaterOrEqual(wild, 0);
            Assert.GreaterOrEqual(mine, 0);
            Assert.Less(wild, mine, "「앗! 야생 …」 → 「가랏! …」");
            // 모든 줄: 같은 갱신 안에서 뒤에 적힌 줄이 앞 줄보다 먼저 드러나지 않는다
            int counted = 0;
            foreach (var update in run.Updates)
            {
                int lastEntry = -1, lastAt = -1;
                string lastKey = null;
                foreach (var line in lines)
                    for (int k = 0; k < update.Count; k++)
                    {
                        if (!FxReveal.TryAt(update[k], line.Event, out int at)) continue;
                        Assert.IsTrue(k > lastEntry || (k == lastEntry && at >= lastAt), lastKey + " (" + lastAt + ") 뒤에 " + line.Key + " (" + at + ")");
                        lastEntry = k;
                        lastAt = at;
                        lastKey = line.Key;
                        counted++;
                        break;
                    }
            }
            Assert.AreEqual(lines.Count, counted, "모든 줄이 어느 항목에서 드러난다");
        }

        [Test]
        public void M4_뒷모습던지기는주인공8명모두표의순서대로이고손을떠나는틱이260ms에온다_결정53()
        {
            string path = Path.Combine(TestData.RepoRoot(), "Assets/Sinnoh/Gen/Trainers/trainers.json");
            if (!File.Exists(path)) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저 (Trainers/trainers.json)");
            string table = File.ReadAllText(path, new UTF8Encoding(false));
            var throws = BackThrow.ParseAll(table);
            var names = new List<string>();
            foreach (var t in throws) names.Add(Path.GetFileNameWithoutExtension(t.File));
            CollectionAssert.AreEquivalent(Protagonists.Keys, names, "던지기 순서가 있는 뒷모습 = 고를 수 있는 주인공 8명(결정 60)");
            Assert.IsNotNull(BackThrow.Find(table, Protagonists.Default), "고르지 않았을 때의 주인공");
            Assert.IsNull(BackThrow.Find(table, "pt_rival"), "던지기 순서가 없는 뒷모습");
            foreach (var t in throws)
            {
                string name = t.File;
                int sum = 0;
                foreach (int n in t.SeqTicks) sum += n;
                Assert.AreEqual(t.Total, sum, name + ": 순서의 틱 합 = total");
                Assert.AreEqual(60, t.Fps, name);
                Assert.That(t.Release, Is.GreaterThan(0).And.LessThan(t.Total), name + " release");
                // 틱 → 프레임: 순서를 한 틱씩 펼친 것과 같다
                int tick = 0;
                for (int i = 0; i < t.SeqFrame.Length; i++)
                    for (int k = 0; k < t.SeqTicks[i]; k++, tick++)
                        Assert.AreEqual(t.SeqFrame[i], t.FrameAtTick(tick), name + " 틱 " + tick);
                Assert.AreEqual(t.Still, t.FrameAtTick(t.Total), name + ": 순서가 끝나면 서 있는 프레임");
                Assert.AreEqual(t.SeqFrame[0], t.FrameAtTick(-5), name);
                // ms → 틱: 손을 떠나는 틱이 260ms(결정 54 ①)에 오고, 그 앞은 고르게, 그 뒤는 60fps
                Assert.AreEqual(0, t.TickAt(0, EnterBeats.ReleaseMs), name);
                Assert.AreEqual(t.Leave, t.TickAt(EnterBeats.ReleaseMs, EnterBeats.ReleaseMs), name + ": 260ms = leave 틱(손 자리를 잰 프레임의 마지막 틱 — 결정 62)");
                Assert.AreEqual(t.FrameAtTick(t.Leave), t.FrameAt(EnterBeats.ReleaseMs, EnterBeats.ReleaseMs), name);
                Assert.AreEqual(t.Leave + 60, t.TickAt(EnterBeats.ReleaseMs + 1000, EnterBeats.ReleaseMs), name + ": 손을 떠난 뒤 1초 = 60틱");
                int prev = 0;
                for (int ms = 0; ms <= 3000; ms += 7)
                {
                    int at = t.TickAt(ms, EnterBeats.ReleaseMs);
                    Assert.GreaterOrEqual(at, prev, name + ": 틱은 뒤로 가지 않는다");
                    prev = at;
                }
                Assert.AreEqual(t.Still, t.FrameAt(t.EndMs(EnterBeats.ReleaseMs), EnterBeats.ReleaseMs), name + ": 던지기가 끝나면 서 있는 프레임(성공하면 이 모습으로 남는다)");
                Assert.Less(t.EndMs(EnterBeats.ReleaseMs), PlayTiming.CaptureMs(false, 0), name + ": 가장 짧은 포획(3100) 안에 던지기가 끝난다");
                // 프레임의 시트 안 자리: cols열 격자, 시트 안
                var (w, h) = PngSize(Path.Combine(TestData.RepoRoot(), "Assets/Sinnoh/Gen/Trainers", t.File));
                foreach (int f in t.SeqFrame)
                {
                    Assert.That(f, Is.GreaterThanOrEqualTo(0).And.LessThan(t.Frames), name + " 프레임 " + f);
                    t.Cell(f, out int x, out int y);
                    Assert.AreEqual((f % t.Cols * t.Fw, f / t.Cols * t.Fh), (x, y));
                    Assert.That(x + t.Fw, Is.LessThanOrEqualTo(w), name + " 시트 폭");
                    Assert.That(y + t.Fh, Is.LessThanOrEqualTo(h), name + " 시트 높이");
                }
                Assert.That(t.OriginX, Is.GreaterThan(0).And.LessThanOrEqualTo(t.Fw), name + " origin(원작 자산이 세우는 자리 — 결정 55 ①)");
                Assert.That(t.OriginY, Is.GreaterThan(0).And.LessThanOrEqualTo(t.Fh), name);
            }
        }

        [Test]
        public void M4_볼은주인공마다표의손자리에서나온다_결정61()
        {
            // 뒷모습 고정점(DsStage.TrainerAt)과 4세대 손 자리(고정점 − origin (40, 80) + 뻗은 손 가운데 (71, 33) — 결정 62에서 그림으로 다시 쟀다) — Client 값을 그대로 적는다(엔진 없는 시험)
            const int anchorX = 22, anchorY = 144, oldX = 53, oldY = 97;
            // 행 읽기: hand 칸 → 손 자리, 화면 자리 = 고정점 − origin + hand
            var row = BackThrow.Parse("{\"set\":\"official\",\"file\":\"Back/official/x.png\",\"source\":\"rom:bw:x\",\"fw\":114,\"fh\":121,\"frames\":3,\"origin\":[56,119],\"foot\":[57,118],"
                + "\"cols\":8,\"fps\":60,\"total\":10,\"release\":4,\"still\":0,\"head\":11,\"hand\":[64,3],\"leave\":3,\"frameAffine\":\"000\",\"sequence\":[[0,2],[1,2],[2,6]]},");
            Assert.IsNotNull(row);
            Assert.AreEqual((64, 3), (row.HandX, row.HandY));
            Assert.AreEqual(3, row.Leave);
            row.HandAt(anchorX, anchorY, out int hx, out int hy);
            Assert.AreEqual((30, 28), (hx, hy));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, row.SeqFrame, "hand 칸 뒤의 순서도 그대로 읽는다");
            // 볼은 손을 떠나는 260ms에 그 자리에서 나온다(그 앞에는 없다 — 결정 54 ① 박자 그대로)
            var c = new CaptureAttempt { Success = true, Shakes = 3, Faces = new[] { AttackDieFace.RegularHit } };
            Assert.IsFalse(CaptureStage.Pose(c, EnterBeats.ReleaseMs - 1, 6000, hx, hy, 192, 92).Ball);
            var first = CaptureStage.Pose(c, EnterBeats.ReleaseMs, 6000, hx, hy, 192, 92);
            Assert.IsTrue(first.Ball);
            Assert.AreEqual((hx, hy), (first.BallX, first.BallY), "첫 볼 자리 = 손");

            // 추적 표: 주인공 8명 모두, 4세대 넷은 같은 자리(프레임 3의 뻗은 손 — 네 시트가 같다)
            string handsPath = Path.Combine(TestData.RepoRoot(), "Tools/assets/tables/throw_hands.json");
            var hands = new Dictionary<string, (int X, int Y)>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(handsPath, new UTF8Encoding(false)), "\\{\"key\":\"([a-z0-9_]+)\",\"hand\":\\[(\\d+),(\\d+)\\],\"why\":\"[^\"]+\"\\}"))
                hands[m.Groups[1].Value] = (int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            CollectionAssert.AreEquivalent(Protagonists.Keys, hands.Keys, "손 자리 표 = 주인공 8명");
            foreach (var kv in hands)
                if (kv.Key.StartsWith("pt_") || kv.Key.StartsWith("hgss_")) Assert.AreEqual((oldX, oldY), (anchorX - 40 + kv.Value.X, anchorY - 80 + kv.Value.Y), kv.Key + ": 4세대는 뻗은 손(origin (40, 80))");

            string path = Path.Combine(TestData.RepoRoot(), "Assets/Sinnoh/Gen/Trainers/trainers.json");
            if (!File.Exists(path)) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저 (Trainers/trainers.json)");
            var throws = BackThrow.ParseAll(File.ReadAllText(path, new UTF8Encoding(false)));
            Assert.AreEqual(8, throws.Count, "hand·leave 칸이 없는 던지기 행은 읽히지 않는다");
            var seen = new HashSet<(int, int)>();
            foreach (var t in throws)
            {
                string key = Path.GetFileNameWithoutExtension(t.File);
                Assert.AreEqual(hands[key], (t.HandX, t.HandY), key + ": Gen 행 = 표");
                Assert.That(t.HandX, Is.GreaterThanOrEqualTo(0).And.LessThan(t.Fw), key);
                Assert.That(t.HandY, Is.GreaterThanOrEqualTo(0).And.LessThan(t.Fh), key);
                t.HandAt(anchorX, anchorY, out int x, out int y);
                Assert.That(x, Is.GreaterThanOrEqualTo(8).And.LessThan(128), key + ": 화면 왼쪽 절반 안(볼 16px이 잘리지 않는다)");
                Assert.That(y, Is.GreaterThanOrEqualTo(8).And.LessThan(144), key + ": 메시지 창 위");
                bool gen4 = t.Fw == 80;
                Assert.AreEqual(gen4, (x, y) == (oldX, oldY), key + ": 4세대만 그 자리");
                if (!gen4)
                {
                    seen.Add((x, y));
                    Assert.Less(y, oldY - 30, key + ": 5세대는 머리 위로 든 손 — 4세대 자리(몸 앞)보다 훨씬 위");
                }
            }
            Assert.Greater(seen.Count, 1, "5세대는 주인공마다 다른 자리");
        }

        [Test]
        public void M4_뒷모습이나와있는동안체력상자는없고실패하면돌아온다_결정62()
        {
            const AttackDieFace R = AttackDieFace.RegularHit;
            // 던지지 않았으면(단계 앞) 체력 상자는 제자리
            var none = CaptureStage.Pose(new CaptureAttempt { Success = true, Shakes = 3, Faces = new[] { R } }, -1, 3960, 50, 90, 192, 92);
            Assert.IsFalse(none.BoxHidden || none.BoxAway != 0 || none.Trainer, "포획 단계 앞");
            Assert.IsFalse(default(CapturePose).BoxHidden, "포획이 없는 장면(기술·교체·기절)은 체력 상자를 건드리지 않는다");
            foreach (var (ok, shakes) in new[] { (true, 3), (false, 2), (false, 1), (false, 0) })
            {
                var c = new CaptureAttempt { Success = ok, Shakes = shakes, Faces = new[] { R, R, R } };
                int total = PlayTiming.CaptureMs(ok, shakes), verdict = CaptureStage.VerdictMs(c);
                string name = (ok ? "성공" : "실패") + " 흔들림 " + shakes;
                CapturePose At(int t) => CaptureStage.Pose(c, t, total, 50, 90, 192, 92);
                // 뒷모습이 들어오는 180ms 동안 옆으로 물러난다(인트로에서 들어온 거리 128 — 상자 폭이라 다 나간다), 뒤로 가지 않는다
                Assert.IsFalse(At(0).BoxHidden);
                Assert.AreEqual(0, At(0).BoxAway, name + ": 던지는 순간에는 제자리");
                int prev = 0;
                for (int t = 0; t < EnterBeats.TrainerInMs; t += 10)
                {
                    var p = At(t);
                    Assert.IsFalse(p.BoxHidden, name);
                    Assert.GreaterOrEqual(p.BoxAway, prev, name + ": 물러나기만 한다");
                    prev = p.BoxAway;
                }
                Assert.That(prev, Is.GreaterThan(EnterBeats.IntroBoxPx / 2).And.LessThanOrEqualTo(EnterBeats.IntroBoxPx), name);
                // 뒷모습이 제자리에 선 뒤로는 그리지 않는다 — 볼이 손을 떠나는 260(5세대의 든 손이 상대 상자 자리다)에도, 판정 때도
                int leave = ok ? int.MaxValue : Math.Max(verdict, total - EnterBeats.LeaveMs);
                foreach (int t in new[] { EnterBeats.TrainerInMs, EnterBeats.ReleaseMs, EnterBeats.FlyEndMs, EnterBeats.BounceEndMs, verdict - 1, verdict, Math.Min(leave, total) - 1 })
                {
                    Assert.IsTrue(At(t).BoxHidden, name + " " + t + "ms: 뒷모습이 서 있는 동안 체력 상자는 없다");
                    Assert.IsTrue(At(t).Trainer && At(t).TrainerDx == 0, name + " " + t + "ms: 뒷모습이 서 있다");
                }
                if (ok)
                {
                    // 성공: 돌아오지 않는다 — 잡은 뒤·결과 창까지(뒷모습이 남는다, 결정 54 ②)
                    foreach (int t in new[] { total - 1, total, total + 5000, int.MaxValue / 2 })
                        Assert.IsTrue(At(t).BoxHidden && At(t).Trainer, name + " " + t + "ms: 숨긴 채 결과 창으로");
                    Assert.IsTrue(CaptureStage.Pose(c, int.MaxValue / 2, 0, 50, 90, 192, 92).BoxHidden, "결과 창 동안(단계 길이를 모를 때)");
                    continue;
                }
                // 실패: 뒷모습이 물러나는 끝 250ms 동안 돌아온다(뒷모습이 물러나기 시작할 때 화면 밖에서 들어오기 시작)
                Assert.AreEqual(total - EnterBeats.LeaveMs, leave, name + ": 물러나는 때 = 단계 끝 250ms 전");
                Assert.IsFalse(At(leave).BoxHidden);
                Assert.AreEqual(EnterBeats.IntroBoxPx, At(leave).BoxAway, name + ": 화면 밖에서");
                Assert.AreEqual(0, At(leave).TrainerDx, name + ": 뒷모습이 물러나기 시작하는 때와 같다");
                prev = EnterBeats.IntroBoxPx;
                for (int t = leave; t <= total; t += 10)
                {
                    var p = At(t);
                    Assert.IsFalse(p.BoxHidden, name);
                    Assert.LessOrEqual(p.BoxAway, prev, name + ": 돌아오기만 한다");
                    prev = p.BoxAway;
                }
                var end = At(total);
                Assert.IsFalse(end.Trainer || end.BoxHidden, name + ": 뒷모습이 다 물러나면");
                Assert.AreEqual(0, end.BoxAway, name + ": 체력 상자는 제자리");
                Assert.IsFalse(At(int.MaxValue / 2).BoxHidden || At(int.MaxValue / 2).BoxAway != 0, name + ": 그 뒤로도");
            }
            // 판정 시각·전체 길이는 그대로다(결정 54 ①)
            Assert.AreEqual((3890, 4790, 3420, 4170), (PlayTiming.CaptureVerdictMs(3), PlayTiming.CaptureMs(true, 3), PlayTiming.CaptureVerdictMs(2), PlayTiming.CaptureMs(false, 2)));
            Assert.AreEqual((270, 390), (EnterBeats.TrainerInMs, EnterBeats.ReleaseMs), "던지기 1.5배(결정 126 ②)");
        }

        [Test]
        public void M4_볼이나타나는순간뒷모습은손자리를잰프레임이다_결정62()
        {
            // 행 읽기: leave 칸 = 손 자리를 잰 프레임이 보이는 마지막 틱. 그 틱이 260ms에 오고 한 틱(1/60초) 뒤에 다음 프레임으로 넘어간다
            var row = BackThrow.Parse("{\"set\":\"official\",\"file\":\"Back/official/x.png\",\"source\":\"rom:bw:x\",\"fw\":114,\"fh\":121,\"frames\":4,\"origin\":[56,119],\"foot\":[57,118],"
                + "\"cols\":8,\"fps\":60,\"total\":20,\"release\":8,\"still\":0,\"head\":11,\"hand\":[64,3],\"leave\":7,\"frameAffine\":\"0000\",\"sequence\":[[0,4],[1,4],[2,2],[3,10]]},");
            Assert.IsNotNull(row);
            Assert.AreEqual((8, 7), (row.Release, row.Leave));
            Assert.AreEqual(1, row.FrameAt(EnterBeats.ReleaseMs - 1, EnterBeats.ReleaseMs), "볼이 나오기 직전: 손을 든 프레임");
            Assert.AreEqual(1, row.FrameAt(EnterBeats.ReleaseMs, EnterBeats.ReleaseMs), "볼이 나타나는 순간: 아직 손을 든 프레임");
            Assert.AreEqual(1, row.FrameAt(EnterBeats.ReleaseMs + 16, EnterBeats.ReleaseMs), "그 틱 동안");
            Assert.AreEqual(2, row.FrameAt(EnterBeats.ReleaseMs + 17, EnterBeats.ReleaseMs), "한 틱 뒤: 팔을 휘두른 프레임");
            Assert.IsNull(BackThrow.Parse("{\"set\":\"official\",\"file\":\"Back/official/x.png\",\"source\":\"rom:bw:x\",\"fw\":114,\"fh\":121,\"frames\":4,\"origin\":[56,119],\"foot\":[57,118],"
                + "\"cols\":8,\"fps\":60,\"total\":20,\"release\":8,\"still\":0,\"head\":11,\"hand\":[64,3],\"sequence\":[[0,4],[1,4],[2,2],[3,10]]},"), "leave 칸이 없는 옛 행은 읽지 않는다(Gen을 다시 만든다)");

            string path = Path.Combine(TestData.RepoRoot(), "Assets/Sinnoh/Gen/Trainers/trainers.json");
            if (!File.Exists(path)) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저 (Trainers/trainers.json)");
            var throws = BackThrow.ParseAll(File.ReadAllText(path, new UTF8Encoding(false)));
            Assert.AreEqual(8, throws.Count);
            foreach (var t in throws)
            {
                string key = Path.GetFileNameWithoutExtension(t.File);
                bool gen4 = t.Fw == 80;
                int held = t.FrameAtTick(t.Leave);
                if (gen4)
                {
                    // 4세대: 뻗은 팔 끝에서 나온다 — release 틱의 프레임(팔을 뻗은 3) 그대로
                    Assert.AreEqual(t.Release, t.Leave, key + ": 4세대 leave = release");
                    Assert.AreEqual(3, held, key + ": 팔을 뻗은 프레임");
                }
                else
                {
                    // 5세대: release 틱에는 이미 팔을 휘두른 프레임이다 — 그 바로 앞(든 손에 볼이 있는 마지막 프레임)에서 볼이 나온다
                    Assert.AreEqual(t.Release - 1, t.Leave, key + ": 5세대 leave = release − 1");
                    Assert.AreNotEqual(t.FrameAtTick(t.Release), held, key + ": release 틱의 프레임은 다음 것(팔을 휘두른)");
                    Assert.AreEqual(held, t.FrameAt(EnterBeats.ReleaseMs - 20, EnterBeats.ReleaseMs), key + ": 볼이 나오기 20ms 전에도 든 손");
                    Assert.AreEqual(t.FrameAtTick(t.Release), t.FrameAt(EnterBeats.ReleaseMs + 17, EnterBeats.ReleaseMs), key + ": 한 틱 뒤에 휘두른다");
                }
                Assert.AreEqual(held, t.FrameAt(EnterBeats.ReleaseMs, EnterBeats.ReleaseMs), key + ": 볼이 나타나는 260ms의 뒷모습 = 손 자리를 잰 프레임");
                Assert.AreEqual(held, t.FrameAt(EnterBeats.ReleaseMs + 16, EnterBeats.ReleaseMs), key);
            }
        }

        // ── M9 다듬기 2: 조우 카드 연출(21행 — 강한 카드 늦게·무겁게 · 마우스 오버 들림 · 덱으로 쓸려 감, 가정 #415~#417) ──

        [Test]
        public void M9_조우카드_가벼운카드만이면옛뒤집기와같다()
        {
            var light = new[] { false, false, false };
            for (int i = 0; i < 3; i++)
                for (int t = 0; t <= 1300; t += 5)
                {
                    bool face = EnterRows.FlipFace(i, t, out float sx);
                    var p = EncounterCards.Reveal(light, i, t);
                    Assert.AreEqual(face, p.Face, i + "번 " + t + "ms");
                    Assert.AreEqual(sx, p.ScaleX, 1e-5, i + "번 " + t + "ms");
                    Assert.AreEqual(1f, p.ScaleY);
                    Assert.AreEqual(0f, p.Rise);
                }
            Assert.AreEqual(EnterRows.FlipAt(2) + EnterBeats.FlipMs / 2, EncounterCards.FaceAt(light, 2), "소리 「flip」 시각도 옛 값");
        }

        [Test]
        public void M9_조우카드_강한카드는가벼운카드가다뒤집힌뒤떠올라느리게뒤집히고쿵떨어져눌렸다편다()
        {
            // 왜곡된 나무(강함 · 보통 · 약함 1장씩 — 강함이 왼쪽에 와도 늦게)
            var mixed = new[] { true, false, false };
            Assert.AreEqual(300, EncounterCards.StartAt(mixed, 1), "가벼운 카드는 가벼운 것끼리 왼쪽부터");
            Assert.AreEqual(550, EncounterCards.StartAt(mixed, 2));
            int start = EncounterCards.StartAt(mixed, 0);
            Assert.AreEqual(550 + EnterBeats.FlipMs + EnterBeats.HeavyLeadMs, start, "가벼운 카드가 다 뒤집힌 뒤 300 더 기다린다");
            Assert.IsTrue(EncounterCards.Reveal(mixed, 2, start).Face, "강한 카드가 움직일 때 가벼운 카드는 모두 앞면");

            var before = EncounterCards.Reveal(mixed, 0, start);
            Assert.IsFalse(before.Face);
            Assert.AreEqual(0f, before.Rise);
            var risen = EncounterCards.Reveal(mixed, 0, start + EnterBeats.HeavyRiseMs);
            Assert.AreEqual(1f, risen.Rise, 1e-5, "예비: 떠올랐다");
            Assert.Greater(risen.ScaleY, 1f, "떠오르며 살짝 커진다");
            Assert.IsFalse(EncounterCards.Reveal(mixed, 0, start + EnterBeats.HeavyFlipFromMs + EnterBeats.HeavyFlipMs / 2 - 1).Face);
            Assert.IsTrue(EncounterCards.Reveal(mixed, 0, start + EnterBeats.HeavyFlipFromMs + EnterBeats.HeavyFlipMs / 2).Face, "느린 뒤집기의 가운데에서 앞면");
            Assert.AreEqual(start + EnterBeats.HeavyFlipFromMs + EnterBeats.HeavyFlipMs / 2, EncounterCards.FaceAt(mixed, 0));
            var falling = EncounterCards.Reveal(mixed, 0, start + EnterBeats.HeavyLandMs - EnterBeats.HeavyDropMs / 2);
            Assert.Greater(falling.Rise, 0f);
            Assert.Less(falling.Rise, 1f, "떨어지는 중");
            int land = EncounterCards.LandAt(mixed, 0);
            Assert.AreEqual(start + EnterBeats.HeavyLandMs, land);
            var hit = EncounterCards.Reveal(mixed, 0, land);
            Assert.AreEqual(0f, hit.Rise, "닿았다");
            Assert.IsTrue(hit.Face);
            Assert.AreEqual(1f - EncounterCards.SquashY, hit.ScaleY, 1e-4, "맞음: 바닥에서 눌린다");
            Assert.AreEqual(1f + EncounterCards.SquashX, hit.ScaleX, 1e-4);
            var rest = EncounterCards.Reveal(mixed, 0, land + EnterBeats.HeavySquashMs);
            Assert.AreEqual(1f, rest.ScaleX, 1e-5, "착지: 펴졌다");
            Assert.AreEqual(1f, rest.ScaleY, 1e-5);
            var passed = EncounterCards.Reveal(mixed, 0, int.MaxValue);
            Assert.IsTrue(passed.Face, "단계가 지난 뒤(「끄기」 포함)는 장식 없는 앞면");
            Assert.AreEqual(0f, passed.Rise);
            Assert.AreEqual(1f, passed.ScaleX);
            Assert.AreEqual(1f, passed.ScaleY);

            // 강한 나무(1장): 가벼운 카드가 없으면 첫 뒤집기 때보다 300 늦게 · 공개 단계(2500) 안에서 끝난다. 무거운 카드 셋도
            Assert.AreEqual(EnterBeats.FlipLeadMs + EnterBeats.HeavyLeadMs, EncounterCards.StartAt(new[] { true }, 0));
            var three = new[] { true, true, true };
            int reveal = PlayTiming.RowMs(new EncounterDrawn { Cards = new GradedCard[3] }, EnterRows.Reveal);
            Assert.AreEqual(2500, reveal);
            Assert.LessOrEqual(EncounterCards.LandAt(three, 2) + EnterBeats.HeavySquashMs, reveal, "무거운 카드 셋도 공개 단계 안");
            Assert.LessOrEqual(EncounterCards.LandAt(mixed, 0) + EnterBeats.HeavySquashMs, reveal);
            Assert.IsTrue(EncounterCards.Heavy(EncounterTier.Strong) && EncounterCards.Heavy(EncounterTier.Legendary));
            Assert.IsFalse(EncounterCards.Heavy(EncounterTier.Weak) || EncounterCards.Heavy(EncounterTier.Moderate));
        }

        [Test]
        public void M9_조우카드_강한카드소리는느린뒤집기가운데와닿는때에단다()
        {
            var drawn = new EncounterDrawn { Cards = new[] { new GradedCard { Grade = EncounterTier.Strong }, new GradedCard { Grade = EncounterTier.Moderate }, new GradedCard { Grade = EncounterTier.Weak } } };
            var entry = new FxEntry();
            var step = new FxStep { Row = EnterRows.Reveal, Layer = FxLayer.Item, StartMs = 100, Ms = PlayTiming.RowMs(drawn, EnterRows.Reveal) };
            step.Events.Add(drawn);
            entry.Steps.Add(step);
            SoundCues.Add(entry, true);
            var heavy = EncounterCards.HeavyOf(step);
            CollectionAssert.AreEqual(new[] { true, false, false }, heavy);
            var got = new List<string>();
            foreach (var cue in entry.Cues) if (cue.IsSound) got.Add(cue.AtMs + " " + cue.Sound);
            got.Sort(string.CompareOrdinal);
            var want = new List<string>
            {
                (100 + EncounterCards.FaceAt(heavy, 0)) + " " + SoundKeys.CardFlip,
                (100 + EncounterCards.LandAt(heavy, 0)) + " " + SoundKeys.Land,
                (100 + 300 + EnterBeats.FlipMs / 2) + " " + SoundKeys.CardFlip,
                (100 + 550 + EnterBeats.FlipMs / 2) + " " + SoundKeys.CardFlip,
            };
            want.Sort(string.CompareOrdinal);
            CollectionAssert.AreEqual(want, got, "가벼운 카드 뒤집기 둘 · 강한 카드 뒤집기 · 쿵(임시 land)");
        }

        [Test]
        public void M9_조우카드_고른뒤나머지는뒷면으로뒤집혀덱으로쓸려간다()
        {
            Assert.AreEqual(1f, EncounterCards.Sweep(1, 1, 300).Opacity, "고른 카드는 그대로");
            Assert.AreEqual(0f, EncounterCards.Sweep(1, 1, int.MaxValue).Away);
            Assert.IsTrue(EncounterCards.Sweep(-1, 0, 300).Face, "아직 안 골랐다");
            var start = EncounterCards.Sweep(1, 0, 0);
            Assert.IsTrue(start.Face);
            Assert.AreEqual(0f, start.Away);
            Assert.IsTrue(EncounterCards.Sweep(1, 0, EnterBeats.SweepFlipMs / 2 - 1).Face, "뒤집히는 앞 절반은 앞면");
            Assert.IsFalse(EncounterCards.Sweep(1, 0, EnterBeats.SweepFlipMs / 2).Face, "가운데에서 뒷면");
            var sliding = EncounterCards.Sweep(1, 0, EnterBeats.SweepFlipMs + EnterBeats.SweepSlideMs / 2);
            Assert.Greater(sliding.Away, 0f);
            Assert.Less(sliding.Opacity, 1f);
            Assert.Less(sliding.ScaleY, 1f, "줄어들며 내려간다");
            Assert.AreEqual(0f, EncounterCards.Sweep(1, 2, EnterBeats.SweepEveryMs).Away, "둘째로 쓸려 가는 카드는 60 늦게");
            int last = EnterBeats.SweepEveryMs + EnterBeats.SweepFlipMs + EnterBeats.SweepSlideMs;
            int picked = PlayTiming.RowMs(new EncounterPicked(), EnterRows.Picked);
            Assert.AreEqual(600, picked);
            Assert.LessOrEqual(last, picked, "「고른 뒤 600」 안에 다 쓸려 간다");
            foreach (int i in new[] { 0, 2 })
            {
                Assert.AreEqual(1f, EncounterCards.Sweep(1, i, last).Away, 1e-5);
                Assert.AreEqual(0f, EncounterCards.Sweep(1, i, int.MaxValue).Opacity, "단계가 지난 뒤(「끄기」 포함)는 덱에 들어가 보이지 않는다");
            }
        }

        [Test]
        public void M9_조우카드_마우스오버들림은연출속도를따르고끄기면없다()
        {
            Tunables.Shared.Reset();
            Assert.AreEqual(EnterBeats.HoverMs, EncounterCards.HoverMs(FxSpeed.Normal));
            Assert.AreEqual(EnterBeats.HoverMs * FxTune.FastPercent.Value / 100, EncounterCards.HoverMs(FxSpeed.Fast), "빠름 = 대기열 시계와 같은 배율");
            Assert.Less(EncounterCards.HoverMs(FxSpeed.Fast), EncounterCards.HoverMs(FxSpeed.Normal));
            Assert.AreEqual(0, EncounterCards.HoverMs(FxSpeed.Off), "끄기: 들리지 않는다(장식 없음)");
            Assert.AreEqual(0f, EncounterCards.Lift(0f, 1f, 0, 120));
            float mid = EncounterCards.Lift(0f, 1f, 60, 120);
            Assert.Greater(mid, 0.5f, "빨리 들렸다가 천천히 멈춘다");
            Assert.Less(mid, 1f);
            Assert.AreEqual(1f, EncounterCards.Lift(0f, 1f, 120, 120));
            Assert.AreEqual(0f, EncounterCards.Lift(1f, 0f, 120, 120), "벗어나면 내려앉는다");
        }

        static (int W, int H) PngSize(string file)
        {
            Assert.IsTrue(File.Exists(file), "없음: " + file);
            var b = new byte[24];
            using (var f = File.OpenRead(file)) Assert.AreEqual(24, f.Read(b, 0, 24));
            int Be(int p) => b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3];
            return (Be(16), Be(20));
        }
    }
}
