using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan §4 「연출 단서에 소리 달기」(결정 66 ④, 04 §8-4): 소리는 이벤트가 아니라 화면이 그리는 단계의 키 프레임에 단다 — 소리 표(<see cref="SoundCues.Table"/>) 교차 ·
    /// 단서 시각 = 박자 함수의 시각 · 같은 시각 같은 키 한 번 · 한 번에 보낸 것 = 잘게 보낸 것 · 무음(Muted)이면 0.
    /// </summary>
    public class FxSoundsTests
    {
        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        // 화면(GameFlow.Built)과 같은 거르기: 그리는 행 · 그릴 것이 있는 단계
        static bool Drawn(FxStep s) => FxBuilt.Draws(s, SoloGame.SeatCount);

        static List<(int At, string Key, int Row)> Sounds(FxEntry e) => e.Cues.Where(c => c.IsSound).Select(c => (c.AtMs, c.Sound, c.Row)).ToList();

        [Test]
        public void M4_소리표는화면이그리는행마다줄이있고빈칸이없다()
        {
            var keys = new HashSet<string>(SoundKeys.All) { SoundKeys.Cry, SoundKeys.CryLow };   // 울음 칸(층 Cry — M9 소리 ③)은 효과음 표가 아니라 울음 표가 낸다
            for (int row = 0; row <= 2000; row++)
                if (FxBuilt.Drawn(row)) Assert.IsTrue(SoundCues.Covers(row), row + "행: 화면이 그리는 행인데 소리 표에 줄이 없다(소리 · 무음 중 하나를 적는다)");
            var seen = new HashSet<(int, string)>();
            foreach (var r in SoundCues.Table)
            {
                string at = r.Row + " " + r.Beat;
                Assert.IsTrue(seen.Add((r.Row, r.Beat)), at + " 두 번");
                Assert.IsFalse(string.IsNullOrEmpty(r.Beat), at + " 박자 이름");
                Assert.IsFalse(string.IsNullOrEmpty(r.Note), at + " 뜻(언제 내나)");
                Assert.IsTrue(r.Me == SoundCues.Silent || keys.Contains(r.Me), at + " 「나」 칸 " + r.Me);
                Assert.IsTrue(r.Others == SoundCues.Silent || r.Others == SoundCues.SameAsMe || keys.Contains(r.Others), at + " 「남·관전」 칸 " + r.Others);
                Assert.IsTrue(FxBuilt.Drawn(r.Row), at + ": 그리지 않는 행에는 소리 줄을 두지 않는다(단계가 없어 키 프레임이 없다)");
                Assert.AreSame(r, SoundCues.Find(r.Row, r.Beat));
            }
            // 쓰는 키는 모두 효과음 표에 있다(표에 없는 키는 재생 층이 경고 뒤 무음으로 넘긴다)
            var used = new HashSet<string>(SoundCues.Table.Select(r => r.Me).Concat(SoundCues.Table.Select(r => r.Others)).Where(k => k != SoundCues.Silent && k != SoundCues.SameAsMe));
            CollectionAssert.IsSubsetOf(used, keys);
            foreach (var r in SoundCues.Table)
                Assert.AreEqual(r.Me == SoundKeys.Cry || r.Me == SoundKeys.CryLow, r.Layer == SoundLayer.Cry, r.Row + " " + r.Beat + ": 울음 칸 ⇔ 층 Cry");
            Assert.AreEqual(SoundCues.Silent, SoundCues.Find(FxRows.MyTurn, "in").Others, "내 턴 컷신은 차례 좌석에만 선다");
            Assert.AreEqual(SoundLayer.Jingle, SoundCues.Find(HealBeats.Row, "full").Layer, "회복은 징글(BGM 더킹)");
            Assert.IsNull(SoundCues.Find(HealBeats.Row, "jingle"), "결정 73 ⑥: 칸마다 내는 회복 소리는 없다(다 찰 때 한 번만)");
            var e = new FxEntry();
            e.Steps.Add(new FxStep { Row = 1, Layer = FxLayer.Item, Ms = 100 });
            SoundCues.Add(e, true);   // 단서를 달지 않는 행(그리지 않는 행)은 그냥 지나간다
            Assert.IsEmpty(Sounds(e));
        }

        [Test]
        public void M4_소리단서시각은그단계의키프레임과같다()
        {
            // 걷기(11행): 예비 150 + 평지 270 + 오르막 270+80 → 끝 걸음이 닿는 770에 착지음(걸음마다는 무음)
            var walk = new FxEntry();
            var moved = new Moved { Path = new[] { 1, 2, 3 }, Levels = new[] { 0, 0, 1 } };
            var walkStep = new FxStep { Row = 110, Layer = FxLayer.Item, StartMs = 200, Ms = PlayTiming.RowMs(moved, 110) };
            walkStep.Events.Add(moved);
            walk.Steps.Add(walkStep);
            SoundCues.Add(walk, true);
            CollectionAssert.AreEqual(new[] { (200 + 770, SoundKeys.Land, 110) }, Sounds(walk));
            Assert.AreEqual(2, FxWalk.At(moved, 770).Landed, "그 시각에 마지막 걸음이 닿는다");
            Assert.AreEqual(1, FxWalk.At(moved, 769).Landed);

            // 포획 성공(68행, 결정 54 ① · 126 ② 던지기 1.5배): 굴림 0 · 볼이 손을 떠남 390 · 열림 900 · 흔들림 2480·2950·3420 · 세기(판정 100 전에 끝 — 250 간격) · 딸깍 3890
            var catchEntry = Capture(true, 3, 0);
            CollectionAssert.AreEqual(new[]
            {
                (0, SoundKeys.DiceRoll, 680), (390, SoundKeys.BallThrow, 680), (900, SoundKeys.BallOpen, 680), (2480, SoundKeys.BallShake, 680), (2950, SoundKeys.BallShake, 680),
                (3290, SoundKeys.Dice, 680), (3420, SoundKeys.BallShake, 680), (3540, SoundKeys.Dice, 680), (3790, SoundKeys.Dice, 680), (3890, SoundKeys.Capture, 680),
                (3890 + SoundCues.FanfareLagMs, SoundKeys.Fanfare, 680),   // 포획 팡파르(M9 소리 ② — 딸깍 뒤)
            }, Sounds(catchEntry));
            var c = (CaptureAttempt)catchEntry.Steps[0].Events[0];
            Assert.IsTrue(CaptureStage.Pose(c, 3890, 4790, 0, 0, 0, 0).BallDark, "딸깍 = 볼이 어두워지는 프레임");
            Assert.IsFalse(CaptureStage.Pose(c, 3889, 4790, 0, 0, 0, 0).BallDark);
            Assert.IsTrue(CaptureStage.Pose(c, 390, 4790, 0, 0, 0, 0).Ball, "볼이 나타나는 프레임에 던지는 소리");
            Assert.IsFalse(CaptureStage.Pose(c, 389, 4790, 0, 0, 0, 0).Ball);
            // 실패(흔들림 1번): 판정 2950에 볼이 열리며 튀어나옴
            var miss = Sounds(Capture(false, 1, 0));
            CollectionAssert.Contains(miss, (2480, SoundKeys.BallShake, 680));
            CollectionAssert.Contains(miss, (2950, SoundKeys.BallOpen, 680));
            Assert.IsFalse(miss.Any(s => s.Key == SoundKeys.Capture || s.Key == SoundKeys.Fanfare), "실패에는 딸깍 · 팡파르가 없다");

            // 공격 굴림(56행) + 바로 이은 면 바꾸기(57행): 던짐 = 시작 + 400 · 멈춤(+1600) 뒤 +450에 넘김 · 세기는 넘김 박자(730) + 250 뒤부터 130마다
            var roll = new FxEntry();
            var rolled = new DiceRolled { Faces = new[] { AttackDieFace.RegularHit, AttackDieFace.StatusSymbol, AttackDieFace.CriticalHit } };
            var rollStep = new FxStep { Row = 560, Layer = FxLayer.Item, StartMs = 500, Ms = PlayTiming.RowMs(rolled, 560) };
            rollStep.Events.Add(rolled);
            var flip = new DiceFacesChanged { Changes = new[] { new FaceChange { Index = 1, Before = AttackDieFace.StatusSymbol, After = AttackDieFace.RegularHit } } };
            var flipStep = new FxStep { Row = 570, Layer = FxLayer.Item, StartMs = rollStep.EndMs, Ms = PlayTiming.RowMs(flip, 570) };
            flipStep.Events.Add(flip);
            roll.Steps.Add(rollStep);
            roll.Steps.Add(flipStep);
            roll.Events.Add(rolled);
            roll.Events.Add(flip);
            SoundCues.Add(roll, true);
            CollectionAssert.AreEqual(new[]
            {
                (900, SoundKeys.DiceRoll, 560), (2950, SoundKeys.Select, 570), (3480, SoundKeys.Dice, 560), (3610, SoundKeys.Dice, 560), (3740, SoundKeys.Dice, 560),
            }, Sounds(roll));
            var flips = new List<DiceFlip>();
            Assert.AreEqual(3480, DiceRollScene.Timeline(roll, flips), "세기 시작 = 굴림판 재생과 같은 식");
            Assert.AreEqual(2950, flips.Single().AtMs);

            // 막 카드(4행): 어두워짐(길이 / 12) 뒤 카드가 떨어지기 시작 · 턴 넘김(101행): 띠 · 내 턴 컷신이 들어오는 때
            var act = new FxEntry();
            act.Steps.Add(new FxStep { Row = ActBeats.Row, Layer = FxLayer.Block, Ms = 3600 });
            act.Steps.Add(new FxStep { Row = FxRows.Handoff, Layer = FxLayer.Band, StartMs = 3600, Ms = 500 });
            act.Steps.Add(new FxStep { Row = FxRows.MyTurn, Layer = FxLayer.Band, StartMs = 4100, Ms = 500 });
            SoundCues.Add(act, true);
            CollectionAssert.AreEqual(new[] { (300, SoundKeys.ActCard, 40), (3600, SoundKeys.Band, FxRows.Handoff), (4100, SoundKeys.MyTurn, FxRows.MyTurn) }, Sounds(act));
            Assert.AreEqual(1f, ActBeats.At(300, 3600, 5).Card, "그 시각에 카드가 보이기 시작한다");
            Assert.AreEqual(0f, ActBeats.At(299, 3600, 5).Card);
            // 남의 항목(「남·관전」 칸): 넘김 띠는 나와 같고 내 턴 컷신은 없다
            var other = new FxEntry();
            other.Steps.Add(new FxStep { Row = FxRows.Handoff, Layer = FxLayer.Band, Ms = 600 });
            other.Steps.Add(new FxStep { Row = FxRows.MyTurn, Layer = FxLayer.Band, StartMs = 600, Ms = 600 });
            SoundCues.Add(other, false);
            CollectionAssert.AreEqual(new[] { (0, SoundKeys.Band, FxRows.Handoff) }, Sounds(other));

            // 인트로(53행): 트레이너 쪽(내 포켓몬 — 뒤에 나온다 +1750)만 볼이 열리고, 야생은 풀숲에서 나와 무음
            var intro = new FxEntry();
            var mine = new SentIn { Seat = 0, Side = 0, PokemonId = 1 };
            var wild = new SentIn { Seat = SeatId.Wild, Side = 1, PokemonId = 2 };
            var introStep = new FxStep { Row = FxRows.Intro, Layer = FxLayer.Item, StartMs = 100, Ms = 3500 };
            introStep.Events.Add(mine);
            introStep.Events.Add(wild);
            intro.Steps.Add(introStep);
            intro.Events.Add(mine);
            intro.Events.Add(wild);
            SoundCues.Add(intro, true);
            CollectionAssert.AreEqual(new[] { (100 + EnterBeats.IntroSecondMs, SoundKeys.BallOpen, FxRows.Intro) }, Sounds(intro));
            Assert.AreEqual(100 + EnterBeats.IntroSecondMs, FxReveal.At(intro, mine), "그 포켓몬이 드러나는 때");

            // 배틀을 연 항목의 첫 출전(결정 99 ②): 주인공의 볼이 손을 떠나는 소리(드러나기 240 전) → 볼이 열리는 소리
            var opened = new FxEntry();
            var openedStep = new FxStep { Row = FxRows.Intro, Layer = FxLayer.Item, StartMs = 100, Ms = 3500 };
            openedStep.Events.Add(mine);
            openedStep.Events.Add(wild);
            opened.Steps.Add(openedStep);
            opened.Events.Add(new BattleStarted());
            opened.Events.Add(mine);
            opened.Events.Add(wild);
            SoundCues.Add(opened, true);
            CollectionAssert.AreEqual(new[] { (100 + EnterBeats.IntroSecondMs - EnterBeats.IntroFlyMs, SoundKeys.BallThrow, FxRows.Intro), (100 + EnterBeats.IntroSecondMs, SoundKeys.BallOpen, FxRows.Intro) }, Sounds(opened));

            // 회복(8·17행, 결정 73 ⑥ — 징글이 들어올 때까지의 임시): 체력이 차는 단계 끝에 한 번(칸마다는 무음) · 회복할 포켓몬이 없으면 무음
            var heal = new FxEntry();
            var healStep = new FxStep { Row = HealBeats.Row, Layer = FxLayer.Item, StartMs = 1000, Ms = 2800 };
            healStep.Events.Add(new Healed { Pokemon = new[] { new HealEntry { Id = 1, HpBefore = 3, HpAfter = 8 } } });
            heal.Steps.Add(healStep);
            SoundCues.Add(heal, true);
            CollectionAssert.AreEqual(new[] { (3800, SoundKeys.Center, 80) }, Sounds(heal));
            var heal3 = new FxEntry();
            var heal3Step = new FxStep { Row = HealBeats.Row, Layer = FxLayer.Item, StartMs = 0, Ms = 2800 };
            heal3Step.Events.Add(new Healed { Pokemon = new[] { new HealEntry { Id = 1, HpBefore = 3, HpAfter = 8 }, new HealEntry { Id = 2, HpBefore = 1, HpAfter = 6 }, new HealEntry { Id = 3, HpBefore = 0, HpAfter = 5 } } });
            heal3.Steps.Add(heal3Step);
            SoundCues.Add(heal3, true);
            CollectionAssert.AreEqual(new[] { (2800, SoundKeys.Center, 80) }, Sounds(heal3), "세 마리여도 다 찰 때 한 번");
            Assert.AreEqual(SoundLayer.Jingle, SoundCues.Find(HealBeats.Row, "full").Layer);
            var none = new FxEntry();
            var noneStep = new FxStep { Row = HealBeats.Row, Layer = FxLayer.Item, Ms = 2800 };
            noneStep.Events.Add(new Healed { Pokemon = new HealEntry[0] });
            none.Steps.Add(noneStep);
            SoundCues.Add(none, true);
            Assert.IsEmpty(Sounds(none));

            // AI 자막(73행): 사유가 있는 답만 띠가 들어오는 때에 작은 톡
            var ai = new FxEntry();
            var aiStep = new FxStep { Row = AiLineBeats.Row, Layer = FxLayer.Item, StartMs = 0, Ms = 1000 };
            aiStep.Events.Add(new ChoiceAnswered { Seat = SeatId.Galactic, ByAi = true, Reason = new AiReason(AiRule.PlanB, 3, 1) });
            ai.Steps.Add(aiStep);
            SoundCues.Add(ai, true);
            CollectionAssert.AreEqual(new[] { (0, SoundKeys.Select, AiLineBeats.Row) }, Sounds(ai));
        }

        [Test]
        public void M5_결정75_우선권토큰을넘긴선턴만상대쪽에놓일때소리가한번난다()
        {
            // 선턴(54행): 토큰을 쓴 선턴만 넘기기(앞 600)가 끝나는 때에 토큰 소리, 그 밖의 선턴은 무음(결정 75 D3 ④ 나)
            FxEntry Order(bool tokenUsed)
            {
                var e = new FxEntry();
                var o = new OrderDecided { Seat = 0, First = 0, TokenUsed = tokenUsed, TokenTo = tokenUsed ? 1 : GameEvent.U };
                var step = new FxStep { Row = 540, Layer = FxLayer.Item, StartMs = 100, Ms = 1800 };
                step.Events.Add(o);
                e.Steps.Add(step);
                e.Events.Add(o);
                SoundCues.Add(e, true);
                return e;
            }
            CollectionAssert.AreEqual(new[] { (100 + InitiativeBeats.PassMs, SoundKeys.Token, 540) }, Sounds(Order(true)));
            CollectionAssert.IsEmpty(Sounds(Order(false)));
        }

        [Test]
        public void M4_결정73_같은칸에걸어들어가면마지막걸음이닿는때만남소리가한번난다()
        {
            // 같은 칸의 말을 만남(13행 — 걷기 항목 끝에 붙는 대기열 밖 행, 결정 73 ⑤): 걸어 들어간 칸에 다른 좌석의 말이 있으면 착지음과 같은 때(「!」가 뜨는 때) 만남 소리 하나
            FxEntry Walk(Func<int, int, bool> occupied, bool mine)
            {
                var e = new FxEntry { Seat = 1 };
                var moved = new Moved { Seat = 1, Path = new[] { 1, 2, 3 }, Levels = new[] { 0, 0, 1 } };
                var step = new FxStep { Row = 110, Layer = FxLayer.Item, StartMs = 200, Ms = PlayTiming.RowMs(moved, 110) };
                step.Events.Add(moved);
                e.Steps.Add(step);
                SoundCues.Add(e, mine, occupied);
                return e;
            }
            var asked = new List<(int, int)>();
            var met = Walk((seat, cell) => { asked.Add((seat, cell)); return cell == 3; }, true);
            CollectionAssert.AreEqual(new[] { (970, SoundKeys.Land, 110), (970, SoundKeys.Meet, 110) }, Sounds(met));
            CollectionAssert.AreEqual(new[] { (1, 3) }, asked, "걸은 좌석 · 도착 칸으로 묻는다");
            Assert.AreEqual(0, MeetBeats.Since(met, 970, out _), "「!」가 뜨는 때");
            Assert.AreEqual(-1, MeetBeats.Since(met, 969, out _));
            CollectionAssert.AreEqual(new[] { (970, SoundKeys.Meet, 110) }, Sounds(Walk((_, c) => c == 3, false)).Where(x => x.Key == SoundKeys.Meet).ToList(), "남의 걸음도 같다(남·관전 = 나)");
            Assert.IsFalse(Sounds(Walk((_, __) => false, true)).Any(x => x.Key == SoundKeys.Meet), "빈 칸이면 없다");
            Assert.IsFalse(Sounds(Walk(null, true)).Any(x => x.Key == SoundKeys.Meet), "칸을 묻지 않으면 없다");
            Assert.AreEqual(SoundCues.SameAsMe, SoundCues.Find(110, "meet").Others);
        }

        static FxEntry Capture(bool success, int shakes, int startMs)
        {
            var e = new FxEntry();
            var c = new CaptureAttempt { Faces = new[] { AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.CriticalHit }, Shakes = shakes, Success = success };
            var s = new FxStep { Row = CaptureStage.Row, Layer = FxLayer.Item, StartMs = startMs, Ms = PlayTiming.RowMs(c, CaptureStage.Row) };
            s.Events.Add(c);
            e.Steps.Add(s);
            e.Events.Add(c);
            SoundCues.Add(e, true);
            return e;
        }

        [Test]
        public void M4_같은시각같은소리는한번이고다시달아도겹치지않는다()
        {
            // 동시 피해(61행 한 단계에 체력 둘): 피격음 한 번
            var e = new FxEntry();
            var s = new FxStep { Row = 612, Layer = FxLayer.Item, StartMs = 40, Ms = 800 };
            s.Events.Add(new HpChanged { PokemonId = 1, Before = 8, After = 5 });
            s.Events.Add(new HpChanged { PokemonId = 2, Before = 8, After = 6 });
            e.Steps.Add(s);
            SoundCues.Add(e, true);
            SoundCues.Add(e, true);
            CollectionAssert.AreEqual(new[] { (40, SoundKeys.Hit, 612) }, Sounds(e));
            // 회복으로 오른 체력은 피격이 아니다
            var up = new FxEntry();
            var u = new FxStep { Row = 612, Layer = FxLayer.Item, Ms = 800 };
            u.Events.Add(new HpChanged { PokemonId = 1, Before = 2, After = 5 });
            up.Steps.Add(u);
            SoundCues.Add(up, true);
            Assert.IsEmpty(Sounds(up));
            // 표에 없는 박자를 달려 하면 던진다(표가 한 곳이다) — 행 번호를 바꾸면 여기서 드러난다
            Assert.IsNull(SoundCues.Find(612, "nope"));
        }

        [Test]
        public void M4_판한바퀴의소리단서는한번에보낸것과잘게보낸것이같고무음이면없다()
        {
            // 찾아 둔 판(볼을 쥐여 준 판): 셋업 → 게임 시작 → 찾아보기 → 조우 → 야생 배틀 → 포획. 화면과 같은 길(짜기 → 그리는 단계만 → 소리 단서)로 항목을 모은다
            var ctx = TestData.LoadCtx();
            var trail = SoloTrail.Catch;
            var pilot = trail.Pilot();
            var session = SoloGame.Start(ctx, new SoloSettings(), trail.Seed, SoloTrail.AiSeed, new PrincipleAgent(), 0, prepare: trail.Prepared ? trail.Prepare : (Action<GameState>)null);
            var client = new SeatClient(session.Link, ctx, SoloGame.Seat);
            var planner = new FxPlanner(SoloGame.Seat, SoloGame.SeatCount);
            var entries = new List<FxEntry>();
            bool caught = false, handed = false;
            client.Updated += up =>
            {
                pilot.Saw(up.Events);
                var planned = planner.Plan(up.Events);
                FxBuilt.KeepSteps(planned, Drawn);
                // 울음(M9 소리 ③): 화면(GameFlow.CryDex)과 같은 셈 — 표 색인이 있으면 그 종, 없으면 보기의 그 포켓몬
                SoundCues.Add(planned, x => x.Seat == SoloGame.Seat || x.Seat < 0, null, (id, def) =>
                {
                    if (def < 0 && id >= 0 && id < client.View.Pokemon.Count) def = client.View.Pokemon[id].Def;
                    return def >= 0 ? ctx.Cards.Pokemon[def].Dex : 0;
                });
                entries.AddRange(planned);
                // 잡은 뒤 다음 턴으로 넘어가는 띠(101행)까지
                if (caught && planned.Any(x => x.Step(FxRows.Handoff) != null)) handed = true;
                foreach (var ev in up.Events) if (ev is CaptureAttempt ca && ca.Success) caught = true;
                if (caught && planned.Any(x => x.Step(FxRows.Handoff) != null)) handed = true;
            };
            long now = 0;
            for (int i = 0, answers = 0; i < 20000 && answers < 400 && !handed; i++)
            {
                var ch = client.FirstChoice();
                if (ch == null)
                {
                    now += 60000;
                    session.Tick(now);
                    continue;
                }
                var legal = client.Legal(ch);
                var a = ch.Kind == ChoiceKind.VillagePick ? legal[trail.Village] : ch.Kind == ChoiceKind.StarterPick ? legal[trail.Starter] : ch.Kind == ChoiceKind.TrainerCardPick ? legal[0] : pilot.Decide(ctx, client.View, ch, legal);
                Assert.IsNotNull(a, ch.Kind.ToString());
                Assert.IsTrue(client.Answer(ch, a, now).Accepted, ch.Kind.ToString());
                answers++;
            }
            client.Close();
            Assert.IsTrue(caught && handed, "포획 뒤 다음 턴까지 가지 못했다");

            var all = entries.SelectMany(x => x.Cues.Where(c => c.IsSound).Select(c => c.Sound)).ToList();
            var dexes = new HashSet<int>(ctx.Cards.Pokemon.Select(p => p.Dex));
            var cries = all.Where(k => SoundKeys.TryCry(k, out _, out _)).ToList();
            Assert.That(cries.Count, Is.GreaterThanOrEqualTo(2), "판 한 바퀴에 울음이 난다(스타터 · 야생이 나옴 · 내 포켓몬이 나옴): " + string.Join(",", cries));
            foreach (string k in cries) { SoundKeys.TryCry(k, out int d, out _); CollectionAssert.Contains(dexes, d, "울음 종 번호는 포켓몬 표의 종: " + k); }
            foreach (string key in new[] { SoundKeys.Land, SoundKeys.Token, SoundKeys.ActCard, SoundKeys.Band, SoundKeys.MyTurn, SoundKeys.Search, SoundKeys.CardFlip, SoundKeys.Wipe,
                         SoundKeys.BallOpen, SoundKeys.DiceRoll, SoundKeys.Dice, SoundKeys.BallThrow, SoundKeys.BallShake, SoundKeys.Capture })
                CollectionAssert.Contains(all, key, "판 한 바퀴에 그 소리가 난다: " + key);
            foreach (var entry in entries)
                foreach (var c in entry.Cues.Where(c => c.IsSound))
                {
                    if (!SoundKeys.TryCry(c.Sound, out _, out _)) CollectionAssert.Contains(SoundKeys.All, c.Sound);
                    Assert.IsTrue(FxBuilt.Drawn(c.Row) || c.Row == 80, c.Row + "행: 그리지 않는 행에 단 소리");
                    Assert.That(c.AtMs, Is.InRange(0, Math.Max(0, entry.LengthMs)), entry.Seq + "번 항목의 " + c.Sound + "가 항목 밖에 있다");
                    Assert.AreEqual(1, entry.Cues.Count(o => o.IsSound && o.AtMs == c.AtMs && o.Sound == c.Sound), "같은 시각 같은 소리 한 번");
                    if (c.Sound == SoundKeys.Token)
                        Assert.IsFalse(entry.Cues.Any(o => o.IsSound && o.Sound == SoundKeys.Token && o.AtMs > c.AtMs && o.AtMs - c.AtMs < SoundCues.TokenGapMs), "토큰 놓기 소리는 120ms에 하나까지");
                }

            // 한 번에 보낸 것과 16ms씩 보낸 것: 같은 소리가 같은 시퀀서 시각에(04 §8-4 「(지난 시각, 지금] 훑기」 — 시작 때 예약하지 않는다)
            List<string> Heard(int stepMs, bool muted)
            {
                var seq = new FxSequencer { Muted = muted };
                var heard = new List<string>();
                seq.Sound += c => heard.Add(seq.CueMs + " " + c.Row + " " + c.Sound);
                seq.Enqueue(entries);
                long total = entries.Sum(x => (long)x.LengthMs) + 100;
                if (stepMs <= 0) seq.Advance((int)total);
                else for (long t = 0; t < total; t += stepMs) seq.Advance(stepMs);
                Assert.IsTrue(seq.IsEmpty);
                return heard;
            }
            var once = Heard(0, false);
            Assert.AreEqual(all.Count, once.Count, "단 소리가 모두 난다");
            CollectionAssert.AreEqual(once, Heard(16, false), "한 번에 보낸 것 = 16ms씩 보낸 것");
            CollectionAssert.AreEqual(once, Heard(333, false), "한 번에 보낸 것 = 333ms씩 보낸 것");
            Assert.IsEmpty(Heard(16, true), "무음(다시 보기 · 건너뛰기)이면 소리 단서가 나지 않는다");
        }

        [Test]
        public void M9_소리표는04행마다한줄이고나와남관전칸이비지않는다()
        {
            // 완료 조건 7 앞부분: 04 §8-8 행 1~102 모두 소리 단서가 있거나 「무음」 · 남·관전 칸이 빈 행 0 · 즉시/연출 구분
            var keys = new HashSet<string>(SoundKeys.All) { SoundKeys.Cry, SoundKeys.CryLow };
            int staged = 0, now = 0, silent = 0;
            for (int row = 1; row <= SoundRows.Count; row++)
            {
                var l = SoundRows.Line(row);
                Assert.IsNotNull(l, row + "행: 소리 표에 줄이 없다(연출 박자 줄 · SoundRows.Explicit 가운데 하나)");
                Assert.AreEqual(row, l.Row);
                Assert.IsFalse(string.IsNullOrEmpty(l.Me), row + "행 「나」 칸이 비었다");
                Assert.IsFalse(string.IsNullOrEmpty(l.Others), row + "행 「남·관전」 칸이 비었다");
                Assert.IsFalse(string.IsNullOrEmpty(l.Note), row + "행 뜻");
                foreach (string k in (l.Me + " · " + l.Others).Split(new[] { " · " }, StringSplitOptions.None))
                    Assert.IsTrue(k == SoundCues.Silent || keys.Contains(k), row + "행: 모르는 키 " + k);
                bool explicitLine = SoundRows.Explicit.Any(x => x.Row == row);
                Assert.AreNotEqual(explicitLine, SoundRows.Beats(row).Count > 0, row + "행: 연출 박자 줄과 따로 적은 줄 가운데 하나만");
                if (l.Timing == SoundTiming.Silent) Assert.IsTrue(l.Me == SoundCues.Silent && l.Others == SoundCues.Silent, row + "행: 무음 줄은 두 칸 다 무음");
                else Assert.IsTrue(l.Me != SoundCues.Silent || l.Others != SoundCues.Silent, row + "행: 소리가 있는 줄은 「나」 · 「남·관전」 가운데 하나가 있다(6행 턴 시작은 남의 턴만)");
                if (l.Timing == SoundTiming.Staged) staged++; else if (l.Timing == SoundTiming.Immediate) now++; else silent++;
            }
            Assert.IsNull(SoundRows.Line(0));
            Assert.IsNull(SoundRows.Line(SoundRows.Count + 1));
            Assert.AreEqual(SoundRows.Explicit.Length, SoundRows.Explicit.Select(x => x.Row).Distinct().Count(), "따로 적은 줄은 행마다 하나");
            foreach (var b in SoundCues.Table)
            {
                int row = b.Row == 110 && b.Beat == "meet" ? 13 : SoundRows.RowOf(b);
                Assert.That(row, Is.InRange(1, SoundRows.Count), b.Row + " " + b.Beat + ": 04 행 밖의 박자");
                CollectionAssert.Contains(SoundRows.Beats(row), b, "모든 박자 줄은 그 행에 모인다");
            }
            // 행 밖의 소리(방 판 핑 · 감정 표현 · 도장 · 응답 시계 · 누르기 · 닫기)
            CollectionAssert.AreEquivalent(new[] { "누르기", "닫기·뒤로", "핑", "감정 표현", "도장", "응답 시계", "튜토리얼 넘김", "튜토리얼 말풍선", "튜토리얼 hint" }, SoundRows.Extras.Select(x => x.Tag).ToArray());
            Assert.AreEqual(SoundKeys.Select, SoundRows.Extras.Single(x => x.Tag == "튜토리얼 hint").Me, "M9 #391: 층이 삼킨 클릭의 hint = select");
            foreach (var x in SoundRows.Extras)
            {
                Assert.IsFalse(string.IsNullOrEmpty(x.Me) || string.IsNullOrEmpty(x.Others), x.Tag);
                foreach (string k in (x.Me + " · " + x.Others).Split(new[] { " · " }, StringSplitOptions.None)) Assert.IsTrue(k == SoundCues.Silent || keys.Contains(k), x.Tag + " " + k);
            }
            Assert.AreEqual(SoundKeys.Timer, SoundRows.Extras.Single(x => x.Tag == "응답 시계").Me, "#221: 남은 10초 = timer(옛 token 대역)");
            Assert.AreEqual(SoundKeys.Ping, SoundRows.Extras.Single(x => x.Tag == "핑").Me, "#273: 핑 = ping(옛 meet 대역)");
            // 울음이 달린 행: 스타터 3 · 내보내기 53·66 · 기절 65(낮춤) · 진화 39 · 폼 바꾸기 79
            foreach (int row in new[] { 3, 39, 53, 65, 66, 79 }) Assert.AreEqual(SoundLayer.Cry, SoundRows.Line(row).Layer, row + "행 층 = 울음");
            StringAssert.Contains(SoundKeys.CryLow, SoundRows.Line(65).Me);
            Assert.AreEqual(1 + SoundRows.Count + SoundRows.Extras.Length, SoundRows.Tsv().TrimEnd('\n').Split('\n').Length, "사람이 볼 표 = 머리 + 102행 + 행 밖");
            Assert.That(staged, Is.GreaterThan(60));
            Assert.AreEqual(SoundRows.Count, staged + now + silent);
            // 사람이 볼 표(소리 ④ 듣기 페이지 — Logs/Review/m9/sound3/sound-table.tsv): SINNOH_SOUND_TABLE=<파일>로 돌리면 쓴다
            string tsv = Environment.GetEnvironmentVariable("SINNOH_SOUND_TABLE");
            if (!string.IsNullOrEmpty(tsv)) System.IO.File.WriteAllText(tsv, SoundRows.Tsv(), new System.Text.UTF8Encoding(false));
        }

        [Test]
        public void M9_울음은포켓몬이드러나는때에그종번호로달린다()
        {
            int Dex(int id, int def) => def >= 0 ? 100 + def : id == 9 ? 900 : 0;
            // 인트로(53행): 내 포켓몬(볼) = 열림 + CryLagMs · 야생 = 드러나는 때(볼 소리는 없다)
            var intro = new FxEntry();
            var mine = new SentIn { Seat = 0, Side = 0, PokemonId = 1, Species = 5 };
            var wild = new SentIn { Seat = SeatId.Wild, Side = 1, PokemonId = 2, Species = 7 };
            var step = new FxStep { Row = FxRows.Intro, Layer = FxLayer.Item, StartMs = 100, Ms = 3500 };
            step.Events.Add(mine);
            step.Events.Add(wild);
            intro.Steps.Add(step);
            intro.Events.Add(mine);
            intro.Events.Add(wild);
            SoundCues.Add(intro, true, null, Dex);
            int mineAt = FxReveal.At(intro, mine), wildAt = FxReveal.At(intro, wild);
            CollectionAssert.AreEquivalent(new[]
            {
                (mineAt, SoundKeys.BallOpen, FxRows.Intro), (mineAt + SoundCues.CryLagMs, SoundKeys.CryKey(105, false), FxRows.Intro), (wildAt, SoundKeys.CryKey(107, false), FxRows.Intro),
            }, Sounds(intro));
            // 울음 찾기가 없으면(옛 부름) 울음은 없다 — 다른 소리는 그대로
            var plain = new FxEntry();
            plain.Steps.Add(step);
            plain.Events.Add(mine);
            plain.Events.Add(wild);
            SoundCues.Add(plain, true);
            CollectionAssert.AreEqual(new[] { (mineAt, SoundKeys.BallOpen, FxRows.Intro) }, Sounds(plain));

            // 기절(65행): 같은 항목의 SentIn으로 종을 찾아 낮춘 울음 — 없으면 화면 보기(Id 9 → 900)
            var faint = new FxEntry();
            var fs = new FxStep { Row = 650, Layer = FxLayer.Item, StartMs = 300, Ms = 900 };
            fs.Events.Add(new Fainted { PokemonId = 2 });
            fs.Events.Add(new Fainted { PokemonId = 9 });
            faint.Steps.Add(fs);
            faint.Events.Add(wild);
            SoundCues.Add(faint, false, null, Dex);
            CollectionAssert.AreEquivalent(new[] { (300, SoundKeys.Faint, 650), (300, SoundKeys.CryKey(107, true), 650), (300, SoundKeys.CryKey(900, true), 650) }, Sounds(faint));
            Assert.IsTrue(SoundKeys.TryCry(SoundKeys.CryKey(107, true), out int d, out bool low) && d == 107 && low);

            // 스타터(3행) · 진화(39행): 도트가 다 드러난 때 · 새 모습이 머무는 때
            var starter = new FxEntry();
            var ss = new FxStep { Row = SetupRows.Starter, Layer = FxLayer.Item, Ms = 2000 };
            ss.Events.Add(new StarterPicked { Species = 3, PokemonId = 4, PartySlot = 0 });
            starter.Steps.Add(ss);
            SoundCues.Add(starter, true, null, Dex);
            CollectionAssert.Contains(Sounds(starter), (2000 * StarterBeats.MonToPermille / 1000, SoundKeys.CryKey(103, false), SetupRows.Starter));
            var evolve = new FxEntry();
            var es = new FxStep { Row = EvolveBeats.Row, Layer = FxLayer.Item, StartMs = 0, Ms = 4800 };
            es.Events.Add(new Evolved { PokemonId = 4, From = 3, To = 8 });
            evolve.Steps.Add(es);
            SoundCues.Add(evolve, true, null, Dex);
            CollectionAssert.Contains(Sounds(evolve), (4800 * EvolveBeats.HoldPermille / 1000, SoundKeys.CryKey(108, false), EvolveBeats.Row));
            // 종을 모르면(0) 울음 없이
            var unknown = new FxEntry();
            unknown.Steps.Add(es);
            SoundCues.Add(unknown, true, null, (_, __) => 0);
            Assert.IsFalse(Sounds(unknown).Any(x => SoundKeys.TryCry(x.Key, out _, out _)));
        }

        [Test]
        public void M9_새효과음키는그행의키프레임에난다()
        {
            FxEntry One(int row, int ms, params GameEvent[] events)
            {
                var e = new FxEntry();
                var s = new FxStep { Row = row, Layer = FxLayer.Item, StartMs = 50, Ms = ms };
                foreach (var ev in events) { s.Events.Add(ev); e.Events.Add(ev); }
                e.Steps.Add(s);
                SoundCues.Add(e, true);
                return e;
            }
            // 피로 칸이 오름(64행 641 — 오를 때만) · 도망(67행 — 기권은 무음) · 귀환 하강음(71행)
            CollectionAssert.AreEqual(new[] { (50, SoundKeys.Exp, 641) }, Sounds(One(641, 600, new FatigueChanged { PokemonId = 1, Before = 0, After = 1 })));
            Assert.IsEmpty(Sounds(One(641, 600, new FatigueChanged { PokemonId = 1, Before = 2, After = 0 })), "피로가 풀리는 것은 무음");
            CollectionAssert.AreEqual(new[] { (50, SoundKeys.Flee, 670) }, Sounds(One(670, 800, new Fled { Side = 0 })));
            Assert.IsEmpty(Sounds(One(670, 800, new Forfeited())));
            CollectionAssert.AreEqual(new[] { (50, SoundKeys.Fall, WhiteoutBeats.Row) }, Sounds(One(WhiteoutBeats.Row, 2000, new Whiteout { FromCell = 3, ToCell = 1 })));
            // 바뀐 대역(M9 소리 ③): 순간이동·게이트·토큰 날기 = warp · 동전 = coin · 스탬프 = stamp · 반짝 = shine · 탈락 = fall
            Assert.AreEqual(SoundKeys.Warp, SoundCues.Find(FateRows.Teleport, "warp").Me);
            Assert.AreEqual(SoundKeys.Warp, SoundCues.Find(UbRows.Gate, "sink").Me);
            Assert.AreEqual(SoundKeys.Warp, SoundCues.Find(UbRows.Fly, "warp").Me);
            Assert.AreEqual(SoundKeys.Coin, SoundCues.Find(UbBattleRows.Coin, "land").Me);
            Assert.AreEqual(SoundKeys.Stamp, SoundCues.Find(StampBeats.Row, "slam").Me);
            Assert.AreEqual(SoundKeys.Shine, SoundCues.Find(ResultRows.Star, "land").Me);
            Assert.AreEqual(SoundKeys.Fall, SoundCues.Find(FallRows.Row, "crack").Me);
        }

        /// <summary>
        /// 결정 143 ④⑥: UB 턴의 UB 이동(82행)은 착지 소리 대신 그 UB의 울음(이동마다 · 내려앉는 프레임), 네크로즈마의 원격 파괴(84행)는 빛을 모으기 시작할 때 지금 판의 네크로즈마 울음(<see cref="SoundCues.ShownNecrozma"/>) — 결정 147 ③부터 빛 모음 · 빛줄기 소리 없이 울음과 칸마다 파괴 소리 destroy만(결정 147 ① — 옛 bang).
        /// 찾아보기 「!」(20행)는 나무 = 풀숲 소리 · 바다 칸(산호) = 물 소리(옛 bang 대신 — bang은 VS 「!」 · 시련 막 깨짐에 남았다).
        /// </summary>
        [Test]
        public void M10_결정143_UB이동과네크로즈마원격파괴는그울음이고찾아보기는나무와산호소리다()
        {
            int Dex(int id, int def) => id == SoundCues.ShownNecrozma ? 10157 : def >= 0 ? 700 + def : 0;
            FxEntry One(int row, int start, int ms, params GameEvent[] events)
            {
                var e = new FxEntry();
                var st = new FxStep { Row = row, Layer = FxLayer.Item, StartMs = start, Ms = ms };
                foreach (var ev in events) { st.Events.Add(ev); e.Events.Add(ev); }
                e.Steps.Add(st);
                SoundCues.Add(e, false, null, Dex);
                return e;
            }
            // UB 이동: 끝 프레임(착지)에 그 UB(포켓몬 표 색인 93 → 793)의 울음 — 착지 소리는 없다
            CollectionAssert.AreEqual(new[] { (100 + 700, SoundKeys.CryKey(793, false), UbRows.Move) },
                Sounds(One(UbRows.Move, 100, 700, new UbMoved { Seat = SeatId.UltraBeast, UbId = 93, From = 1, To = 2 })));
            Assert.AreEqual(SoundLayer.Cry, SoundCues.Find(UbRows.Move, "cry").Layer);
            Assert.IsNull(SoundCues.Find(UbRows.Move, "land"), "UB 이동의 옛 착지 줄은 없다");
            // 네크로즈마 원격 파괴: 지금 판의 네크로즈마 폼 울음만(dexOf가 ShownNecrozma로 받는다) — 결정 147 ③ 빛 모음 소리는 뺐다
            CollectionAssert.AreEqual(new[] { (0, SoundKeys.CryKey(10157, false), UbRows.Strike) },
                Sounds(One(UbRows.Strike, 0, 1500, new NecrozmaStrike { Seat = SeatId.UltraBeast, Cells = new[] { 4 }, Count = 1 })));
            Assert.IsNull(SoundCues.Find(UbRows.Strike, "glow"), "결정 147 ③: 빛 모음 줄은 없다");
            // 울음 찾기가 없으면 아무 소리도 없다
            var plain = new FxEntry();
            var ps = new FxStep { Row = UbRows.Strike, Layer = FxLayer.Item, Ms = 1500 };
            ps.Events.Add(new NecrozmaStrike { Seat = SeatId.UltraBeast, Cells = new[] { 4 }, Count = 1 });
            plain.Steps.Add(ps);
            SoundCues.Add(plain, true);
            CollectionAssert.IsEmpty(Sounds(plain));
            // 네크로즈마의 원격 파괴 칸(ByUbId < 0)도 UB 파괴와 같은 destroy 하나 — 빛줄기 소리(옛 beam = wipe)는 뺐다(결정 147 ③)
            int dark = 1500 * DestroyBeats.DarkFromPermille / 1000;
            CollectionAssert.AreEqual(new[] { (dark, SoundKeys.Destroy, UbRows.Destroy) },
                Sounds(One(UbRows.Destroy, 0, 1500, new CellDestroyed { Seat = SeatId.UltraBeast, Cell = 4, TokenKind = TokenKind.ApricornTree, ByUbId = -1 })));
            Assert.IsNull(SoundCues.Find(UbRows.Destroy, "beam"), "결정 147 ③: 빛줄기 줄은 없다");
            // 결정 147 ①: 칸 파괴만 새 키 destroy — VS 「!」 · 시련 막 깨짐은 bang 그대로
            CollectionAssert.Contains(SoundKeys.All, SoundKeys.Destroy);
            Assert.AreEqual(SoundKeys.Bang, SoundCues.Find(VsBeats.Row, "bang").Me);
            Assert.AreEqual(SoundKeys.Bang, SoundCues.Find(UbBattleRows.Trial, "crack").Me);
            // 찾아보기: 숲 · 평원(나무) = 풀숲 소리, 바다(산호) = 물 소리 — bang은 나지 않는다
            CollectionAssert.AreEqual(new[] { (50, SoundKeys.Search, EnterRows.Search) }, Sounds(One(EnterRows.Search, 50, 1750, new Searched { Seat = 0, Cell = 3, Biome = Biome.Forest })));
            CollectionAssert.AreEqual(new[] { (50, SoundKeys.Search, EnterRows.Search) }, Sounds(One(EnterRows.Search, 50, 1750, new Searched { Seat = 0, Cell = 3, Biome = Biome.Plains })));
            CollectionAssert.AreEqual(new[] { (50, SoundKeys.SearchReef, EnterRows.Search) }, Sounds(One(EnterRows.Search, 50, 1750, new Searched { Seat = 0, Cell = 3, Biome = Biome.Ocean })));
            Assert.IsNull(SoundCues.Find(EnterRows.Search, "bang"));
            CollectionAssert.Contains(SoundKeys.All, SoundKeys.Search);
            CollectionAssert.Contains(SoundKeys.All, SoundKeys.SearchReef);
        }

        /// <summary>M9 소리 ②: 곡 시퀀스의 징글(회복 SEQ_ASA · 시선 곡 도입부 · 진화 곡 · 팡파르 · 교환 곡)이 그 행의 키 프레임에 달리고 층이 징글(BGM 더킹)이다. 균열 패배 「The End」는 곡 상황이라 단서는 무음.</summary>
        [Test]
        public void M9_소리_징글은그행의키프레임에나고층이징글이다()
        {
            FxEntry One(bool mine, int row, int ms, params GameEvent[] events)
            {
                var e = new FxEntry();
                var s = new FxStep { Row = row, Layer = FxLayer.Item, StartMs = 50, Ms = ms };
                foreach (var ev in events) { s.Events.Add(ev); e.Events.Add(ev); }
                e.Steps.Add(s);
                SoundCues.Add(e, mine);
                return e;
            }
            // 회복 8·17행 = 포켓몬센터 징글 · 같은 칸 만남 13행 = 시선 곡 도입부
            Assert.AreEqual((SoundKeys.Center, SoundLayer.Jingle), (SoundCues.Find(HealBeats.Row, "full").Me, SoundCues.Find(HealBeats.Row, "full").Layer));
            Assert.AreEqual((SoundKeys.Meet, SoundLayer.Jingle), (SoundCues.Find(110, "meet").Me, SoundCues.Find(110, "meet").Layer));
            Assert.AreEqual(SoundLayer.Jingle, SoundRows.Line(8).Layer);
            Assert.AreEqual(SoundLayer.Jingle, SoundRows.Line(13).Layer);
            Assert.AreEqual(SoundLayer.Jingle, SoundRows.Line(17).Layer);
            // 진화 39행: 본인은 단계 처음부터 진화 곡 → 빛(700‰) → 빛이 걷힌 때(900‰) 팡파르 · 남은 곡 없이 팡파르만
            var evolved = new Evolved { PokemonId = 4, From = 3, To = 8 };
            CollectionAssert.AreEqual(new[] { (50, SoundKeys.Evolve, EvolveBeats.Row), (50 + EvolveBeats.FlashAt(4800), SoundKeys.BallOpen, EvolveBeats.Row), (50 + 4800 * EvolveBeats.ClearPermille / 1000, SoundKeys.Fanfare, EvolveBeats.Row) },
                Sounds(One(true, EvolveBeats.Row, 4800, evolved)));
            var others = Sounds(One(false, EvolveBeats.Row, 2000, evolved));
            Assert.IsFalse(others.Any(x => x.Key == SoundKeys.Evolve), "남 화면의 진화 알림에는 진화 곡이 없다");
            CollectionAssert.Contains(others, (50 + 2000 * EvolveBeats.ClearPermille / 1000, SoundKeys.Fanfare, EvolveBeats.Row));
            foreach (string beat in new[] { "song", "fanfare" }) Assert.AreEqual(SoundLayer.Jingle, SoundCues.Find(EvolveBeats.Row, beat).Layer, beat);
            // 포획 68행 팡파르 · 교환 52행 곡
            Assert.AreEqual((SoundKeys.Fanfare, SoundLayer.Jingle), (SoundCues.Find(CaptureStage.Row, "fanfare").Me, SoundCues.Find(CaptureStage.Row, "fanfare").Layer));
            Assert.AreEqual((SoundKeys.Trade, SoundLayer.Jingle), (SoundCues.Find(TradeBeats.Row, "song").Me, SoundCues.Find(TradeBeats.Row, "song").Layer));
            StringAssert.Contains(SoundKeys.Fanfare, SoundRows.Line(68).Me);
            StringAssert.Contains(SoundKeys.Evolve, SoundRows.Line(39).Me);
            StringAssert.Contains(SoundKeys.Trade, SoundRows.Line(52).Me);
            // 균열 패배 86행: 「The End」는 곡 상황(GameOver)이 튼다 — 단서는 무음
            Assert.AreEqual(SoundCues.Silent, SoundCues.Find(UbRows.Lost, "end").Me);
            StringAssert.Contains("GameOver", SoundCues.Find(UbRows.Lost, "end").Note);
            foreach (string key in new[] { SoundKeys.Center, SoundKeys.Evolve, SoundKeys.Fanfare, SoundKeys.Trade }) CollectionAssert.Contains(SoundKeys.All, key);
        }
    }
}
