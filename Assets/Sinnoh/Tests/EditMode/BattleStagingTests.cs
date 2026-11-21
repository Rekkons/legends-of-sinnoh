using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 배틀 연출의 순수 부분(plan §4 「배틀 연출」, 04 §8-3·§8-9): 이벤트가 화면에 드러나는 때(<see cref="FxReveal"/>), 드러난 모습(<see cref="BattleFeed.See"/>),
    /// 굴림판의 세기·계산 줄(<see cref="BattleRoll"/>), DS 쪽 움직임(<see cref="BattlePose"/>).
    /// 판은 화면과 같은 길로 만든다 — 솔로 게임을 링크로 답해 [도전](1막 조무래기 배틀), 기술은 늘 첫 줄. 갱신마다 화면 대기열에 넣고 시각을 조금씩 보낸다.
    /// </summary>
    public class BattleStagingTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            ctx = TestData.LoadCtx();
        }

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        sealed class Run
        {
            public SeatClient Client;
            public LocalSession Session;
            public BattleFeed Feed;
            public readonly FxPlanner Planner = new FxPlanner(SoloGame.Seat, SoloGame.SeatCount);
            public readonly FxSequencer Seq = new FxSequencer();
            public readonly List<List<FxEntry>> Updates = new List<List<FxEntry>>();
            public readonly HashSet<GameEvent> Hidden = new HashSet<GameEvent>();
            public long Now;

            public BattleSeen See()
            {
                FxReveal.Hidden(Seq, Hidden);
                return Feed.See(Hidden);
            }
        }

        // 셋업 → [도전]까지. 배틀이 시작된 갱신부터 화면과 같은 길: 짜기 → 그리는 단계만 남기기 → 따라가기 → 대기열에 넣기
        Run Challenge(ulong seed, int village, int starter)
        {
            var r = new Run { Session = SoloGame.Start(ctx, new SoloSettings(), seed, 99, new PrincipleAgent(), 0) };
            r.Client = new SeatClient(r.Session.Link, ctx, SoloGame.Seat);
            r.Client.Updated += u =>
            {
                var entries = r.Planner.Plan(u.Events);
                if (r.Feed == null)
                    foreach (var e in u.Events)
                        if (e is BattleStarted) r.Feed = new BattleFeed(ctx, SoloGame.Seat);
                if (r.Feed == null) return;
                r.Feed.Feed(u.View, u.Events);
                FxBuilt.KeepSteps(entries, s => BattleRows.Built(s.Row) && BattleRows.Draws(s));
                r.Seq.Enqueue(entries);
                r.Updates.Add(entries);
            };
            foreach (var kind in new[] { ChoiceKind.VillagePick, ChoiceKind.StarterPick, ChoiceKind.TrainerCardPick })
            {
                var c = r.Client.Choice(kind);
                int pick = kind == ChoiceKind.VillagePick ? village : kind == ChoiceKind.StarterPick ? starter : 0;
                Assert.IsTrue(r.Client.Answer(c, r.Client.Legal(c)[pick], 0).Accepted, kind.ToString());
            }
            var map = r.Client.Choice(ChoiceKind.MapAction);
            Assert.IsTrue(r.Client.Answer(map, r.Client.Legal(map).Find(a => a.Kind == AnswerKind.Challenge), 0).Accepted);
            Assert.IsNotNull(r.Feed);
            return r;
        }

        // 내 기술 고르기가 열릴 때까지(상대의 답 · 우선권 토큰) 세션을 보내고 화면 대기열을 끝까지 돌린다. 배틀이 끝나면 false
        bool ToMyMove(Run r)
        {
            for (int i = 0; i < 400 && !r.Feed.Ended; i++)
            {
                var c = r.Client.FirstChoice();
                if (c != null && c.Kind == ChoiceKind.BattleAction) break;
                if (c != null && c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.InitiativeToken) Assert.IsTrue(r.Client.Answer(c, r.Client.Legal(c).Find(a => a.Kind == AnswerKind.Accept), r.Now).Accepted);
                else
                {
                    r.Now += 60000;
                    r.Session.Tick(r.Now);
                }
            }
            r.Seq.Advance(600000);
            return !r.Feed.Ended;
        }

        // 내 첫 기술을 낸다. 돌려주는 값 = 그 답의 갱신에서 굴림 단계가 든 첫 항목(내 공격)
        FxEntry Attack(Run r)
        {
            int at = r.Updates.Count;
            var c = r.Client.Choice(ChoiceKind.BattleAction);
            Assert.IsTrue(r.Client.Answer(c, r.Client.Legal(c).Find(a => a.Kind == AnswerKind.UseMove), r.Now).Accepted);
            Assert.Greater(r.Updates.Count, at);
            foreach (var entry in r.Updates[at])
                if (BattleRoll.StepOf(entry) != null) return entry;
            Assert.Fail("공격의 갱신에 굴림 단계가 없다");
            return null;
        }

        static T Event<T>(FxEntry entry) where T : GameEvent
        {
            foreach (var e in entry.Events) if (e is T t) return t;
            return null;
        }

        [Test]
        public void M4_이벤트는제단계가시작할때드러나고값은단계끝에드러난다()
        {
            var (seed, village, starter) = BattleFeedTests.LoseGame;
            var r = Challenge(seed, village, starter);
            Assert.IsTrue(ToMyMove(r));
            var entry = Attack(r);

            var roll = entry.Step(560);
            var move = entry.Step(590);
            var calc = entry.Step(611);
            Assert.IsNotNull(roll);
            Assert.IsNotNull(move);
            Assert.IsNotNull(calc);
            Assert.AreEqual(0, FxReveal.At(entry, Event<MoveSelected>(entry)), "단계가 없는 이벤트는 앞 단계가 없으면 항목 시작");
            Assert.AreEqual(roll.EndMs, FxReveal.At(entry, Event<DiceRolled>(entry)), "주사위 줄(눈 · 합)은 세기가 끝난 뒤");
            Assert.AreEqual(move.StartMs, FxReveal.At(entry, Event<MoveUsed>(entry)), "「○○의 △△!」는 기술 단계가 시작할 때");
            Assert.AreEqual(calc.StartMs + PlayTiming.DamageCalcMs.Value, FxReveal.At(entry, Event<DamageBreakdown>(entry)), "피해 줄은 총합 칸이 켜질 때");
            var hp = Event<HpChanged>(entry);
            if (hp != null)
            {
                var drain = entry.Step(612);
                Assert.AreEqual(drain.EndMs, FxReveal.At(entry, hp), "체력은 깎이는 단계가 끝날 때(착지)");
                var cue = entry.Cues.Find(c => !c.IsSound && c.Key.Equals(ShownKey.Hp(hp.PokemonId)));
                Assert.AreEqual(drain.EndMs, cue.AtMs, "표시 값 단서와 같은 때");
            }
            // 드러나는 때는 받은 순서를 거스르지 않는다(기록 줄이 뒤섞이지 않는다) — 한 박자에 묶인 것만 빼고
            foreach (var e in entry.Events) Assert.LessOrEqual(FxReveal.At(entry, e), entry.LengthMs, e.GetType().Name + ": 항목 안에서 드러난다");
        }

        [Test]
        public void M4_화면에드러난모습은대기열시각을따라가고끝나면받은것과같다()
        {
            foreach (var game in new[] { BattleFeedTests.LoseGame, BattleFeedTests.WinGame })
            {
                var r = Challenge(game.Seed, game.Village, game.Starter);
                int attacks = 0, landed = 0, faints = 0;
                while (ToMyMove(r))
                {
                    Assert.IsTrue(r.Seq.IsEmpty);
                    var before = r.See();
                    Assert.AreEqual(r.Feed.Lines.Count, before.Lines, "대기열이 비면 받은 줄이 다 드러나 있다");
                    int linesBefore = before.Lines;
                    var entry = Attack(r);
                    attacks++;
                    var hit = Event<HpChanged>(entry);
                    int foe = hit != null ? hit.PokemonId : -1;

                    // 누른 직후: 받은 것은 이미 마지막 상태지만 화면에는 아무것도 드러나지 않았다
                    var seen = r.See();
                    Assert.AreEqual(linesBefore, seen.Lines, "답을 낸 직후에는 새 줄이 없다");
                    Assert.AreSame(before.Damage, seen.Damage, "계산 줄은 앞 공격의 것 그대로");
                    Assert.AreEqual(before.Active[0], seen.Active[0]);
                    Assert.AreEqual(before.Active[1], seen.Active[1]);
                    if (hit != null) Assert.AreEqual(hit.Before, r.Seq.Shown.Get(ShownKey.Hp(foe), -1), "체력은 앞 값을 붙잡고 있다");

                    int last = linesBefore;
                    bool sawMove = false, sawHp = false, played = false;
                    while (!r.Seq.IsEmpty)
                    {
                        r.Seq.Advance(40);
                        seen = r.See();
                        Assert.GreaterOrEqual(seen.Lines, last, "드러난 줄은 줄지 않는다");
                        last = seen.Lines;
                        if (!ReferenceEquals(r.Seq.Current, entry))
                        {
                            // 항목이 끝났다(체력 단계가 마지막이면 착지와 함께 끝난다): 그 피해의 체력 줄이 떠 있다
                            if (played && hit != null && hit.Before != hit.After && ReferenceEquals(seen.Damage, Event<DamageBreakdown>(entry)))
                            {
                                Assert.AreSame(hit, seen.DamageHp, "항목이 끝나면 체력 줄이 떠 있다");
                                sawHp = true;
                            }
                            continue;
                        }
                        played = true;
                        int t = r.Seq.LocalMs;
                        var move = entry.Step(590);
                        if (move != null && t >= move.StartMs && !sawMove)
                        {
                            sawMove = true;
                            Assert.Greater(seen.Lines, linesBefore, "기술 단계가 시작하면 「○○의 △△!」가 붙는다");
                        }
                        if (hit != null && hit.Before != hit.After)
                        {
                            var drain = entry.Step(612);
                            int shown = r.Seq.Shown.Get(ShownKey.Hp(foe), -1);
                            if (t < drain.EndMs)
                            {
                                Assert.AreEqual(hit.Before, shown, "착지 전에는 옛 체력");
                                Assert.IsNull(ReferenceEquals(seen.Damage, Event<DamageBreakdown>(entry)) ? seen.DamageHp : null, "체력 줄은 착지 전에 뜨지 않는다");
                                var pose = BattlePose.Of(r.Seq, foe, 1, true);
                                if (t >= drain.StartMs)
                                {
                                    Assert.IsTrue(pose.Draining, "깎이는 단계에서는 막대가 움직인다");
                                    Assert.LessOrEqual(pose.Bar, hit.Before);
                                    Assert.GreaterOrEqual(pose.Bar, hit.After);
                                }
                                else Assert.IsFalse(pose.Draining);
                            }
                            else
                            {
                                Assert.AreEqual(hit.After, shown, "착지 뒤에는 새 체력");
                                Assert.AreSame(hit, seen.DamageHp, "체력 줄이 뜬다");
                                sawHp = true;
                            }
                        }
                        var fainted = Event<Fainted>(entry);
                        if (fainted != null && entry.Step(650) != null)
                        {
                            var step = entry.Step(650);
                            int side = fainted.Side;
                            if (t < step.StartMs + BattleBeats.FaintSinkMs) Assert.IsFalse(seen.Out[side], "내려가기 전에는 무대에 있다");
                            else Assert.IsTrue(seen.Out[side] || seen.Active[side] != fainted.PokemonId, "내려간 뒤에는 무대에 없다(다음 포켓몬이 나오기 전까지)");
                            if (t >= step.StartMs && t < step.StartMs + BattleBeats.FaintSinkMs) Assert.Greater(BattlePose.Of(r.Seq, fainted.PokemonId, side, side == 1).Sink, -1f);
                            faints = Math.Max(faints, 1);
                        }
                    }
                    if (hit != null && hit.Before != hit.After)
                    {
                        Assert.IsTrue(sawHp, "착지를 지났다");
                        landed++;
                    }
                    Assert.IsTrue(sawMove);
                    Assert.AreEqual(0, r.Seq.Shown.Count, "대기열이 비면 붙잡은 값이 없다(보기와 같다)");
                }
                var end = r.See();
                Assert.IsTrue(r.Feed.Ended);
                Assert.IsTrue(end.Ended, "끝까지 보이면 끝이 드러난다");
                Assert.AreEqual(r.Feed.Lines.Count, end.Lines);
                Assert.Greater(attacks, 0);
                Assert.Greater(landed, 0, "체력 착지를 본 공격이 있다");
            }
        }

        [Test]
        public void M4_그리는단계만남기면쓰러진뒤의빈교체는없고결과행은배틀끝항목의마지막이다()
        {
            var (seed, village, starter) = BattleFeedTests.LoseGame;
            var r = Challenge(seed, village, starter);
            while (ToMyMove(r)) Attack(r);
            bool sawEnd = false;
            foreach (var update in r.Updates)
                foreach (var entry in update)
                {
                    foreach (var s in entry.Steps)
                    {
                        Assert.IsTrue(BattleRows.Built(s.Row), "남은 단계 " + s.Row + "는 배틀 화면이 그리는 행이다");
                        if (s.Row == FxRows.SendIn) Assert.IsTrue(s.Events.Exists(e => e is SentIn || (e is SwitchedOut so && so.Reason != SwitchReason.Fainted && so.Reason != SwitchReason.BattleEnd)), "66행은 나오거나 불러들일 때만");
                    }
                    if (Event<BattleEnded>(entry) != null)
                    {
                        sawEnd = true;
                        // 결과·보상(70행)은 결과 창이 그린다 — 배틀 끝이 든 항목의 마지막 단계다(그 뒤는 같은 창의 막 목표 줄 94행뿐). 귀환(71행)은 아직 부품이 없다
                        var end = entry.Step(700);
                        Assert.IsNotNull(end, "결과 단계");
                        Assert.AreEqual(PlayTiming.RowMs(Event<BattleEnded>(entry), 700), end.Ms, "결과 단계 길이 = 방장 값(3000 + 500 × 받은 것)");
                        foreach (var s in entry.Steps) if (s.StartMs > end.StartMs) Assert.AreEqual(940, s.Row, "결과 단계 뒤는 막 목표뿐");
                        Assert.AreEqual(end.StartMs, FxReveal.At(entry, Event<BattleEnded>(entry)), "배틀 끝은 결과 단계가 시작할 때 드러난다(결과 창이 뜬다)");
                        Assert.IsNull(entry.Step(710));
                    }
                }
            Assert.IsTrue(sawEnd);
        }

        // 찾아 둔 판을 길잡이로 첫 배틀이 끝날 때까지 돈다. 배틀이 시작된 갱신부터 화면과 같은 길(짜기 → 그리는 단계만 남기기 — GameFlow와 같은 규칙)로 항목을 모은다
        (BattleFeed Feed, List<FxEntry> Entries) Trail(SoloTrail trail)
        {
            BattleFeed feed = null;
            var entries = new List<FxEntry>();
            var pilot = trail.Pilot();
            var session = SoloGame.Start(ctx, new SoloSettings(), trail.Seed, SoloTrail.AiSeed, new PrincipleAgent(), 0, prepare: trail.Prepared ? trail.Prepare : (Action<GameState>)null);
            var client = new SeatClient(session.Link, ctx, SoloGame.Seat);
            var planner = new FxPlanner(SoloGame.Seat, SoloGame.SeatCount);
            client.Updated += u =>
            {
                pilot.Saw(u.Events);
                var planned = planner.Plan(u.Events);
                if (feed == null)
                    foreach (var e in u.Events)
                        if (e is BattleStarted) feed = new BattleFeed(ctx, SoloGame.Seat);
                if (feed == null) return;
                feed.Feed(u.View, u.Events);
                FxBuilt.KeepSteps(planned, EnterStagingTests.Built);
                entries.AddRange(planned);
            };
            long now = 0;
            for (int i = 0, answers = 0; i < 20000 && answers < 400 && (feed == null || !feed.Ended); i++)
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
            Assert.IsTrue(feed != null && feed.Ended, trail.Name + ": 첫 배틀이 끝나지 않았다");
            return (feed, entries);
        }

        [Test]
        public void M4_상태이상배지와도망은제연출이닿은뒤에드러난다()
        {
            // 04 §8-9 「드러나는 때」: 상태 이상 배지는 그 「걸림」(62행)이 드러날 때까지 앞 상태를 붙잡고, 도망(67행)은 야생이 옆으로 빠지는 600 뒤에 무대에서 빠진다.
            // 산호 판: 내 포켓몬이 졸음에 걸리고 쓰러져 야생이 도망친다
            var (feed, entries) = Trail(SoloTrail.Coral);
            var hidden = new HashSet<GameEvent>();
            var firstStatus = new HashSet<int>();
            int statuses = 0, fled = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                foreach (var e in entry.Events)
                {
                    var applied = e as StatusApplied;
                    var ran = e as Fled;
                    if (applied != null && (applied.Result != StatusResult.Applied || !firstStatus.Add(applied.PokemonId))) applied = null;   // 같은 포켓몬의 첫 걸림만(풀림은 이벤트가 없다)
                    if (applied == null && ran == null) continue;
                    int at = FxReveal.At(entry, e);
                    if (ran != null)
                    {
                        var step = entry.Step(670);
                        Assert.IsNotNull(step, "도망 단계(67행)");
                        Assert.AreEqual(600, BattleBeats.FledMs);
                        Assert.AreEqual(step.StartMs + BattleBeats.FledMs, at, "도망은 옆으로 빠지는 600 뒤에 드러난다");
                    }
                    // 그 항목부터 대기열에 넣고 드러나기 1ms 전과 드러난 때를 본다
                    var seq = new FxSequencer();
                    seq.Enqueue(entries.GetRange(i, entries.Count - i));
                    if (at > 0)
                    {
                        seq.Advance(at - 1);
                        Assert.AreSame(entry, seq.Current);
                    }
                    FxReveal.Hidden(seq, hidden);
                    Assert.IsTrue(hidden.Contains(e), e.GetType().Name + ": 드러나기 전");
                    var before = feed.See(hidden);
                    seq.Advance(1);
                    FxReveal.Hidden(seq, hidden);
                    Assert.IsFalse(hidden.Contains(e), e.GetType().Name + ": 드러난 뒤");
                    var after = feed.See(hidden);
                    if (applied != null)
                    {
                        // 보기는 이미 걸린 상태다(view = 그 상태) — 화면은 연출이 닿기 전까지 앞 상태를 그린다
                        Assert.AreEqual(StatusEffect.None, before.Status(applied.PokemonId, applied.Status), "걸림이 드러나기 전에는 배지가 없다");
                        Assert.AreEqual(applied.Status, after.Status(applied.PokemonId, applied.Status), "드러나면 배지가 뜬다");
                        Assert.AreEqual(before.Lines + 1, after.Lines, "「○○은(는) …!」 줄도 그때 붙는다");
                        statuses++;
                    }
                    else
                    {
                        Assert.IsFalse(before.Out[ran.Side], "빠지기 전에는 야생이 무대에 있다");
                        Assert.IsTrue(after.Out[ran.Side], "빠진 뒤에는 무대에 없다");
                        Assert.Greater(after.Lines, before.Lines, "「도망쳤다!」 줄도 그때 붙는다");
                        fled++;
                    }
                }
            }
            Assert.Greater(statuses, 0, "상태 이상이 걸린 판");
            Assert.AreEqual(1, fled, "야생이 도망친 판");
        }

        [Test]
        public void M4_굴림판은센주사위까지의면과합을보이고계산줄은왼쪽부터켜진다()
        {
            const AttackDieFace S = AttackDieFace.StatusSymbol, R = AttackDieFace.RegularHit, C = AttackDieFace.CriticalHit;
            var events = new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.BattleAction, Auto = AutoKind.Human },
                new DiceRolled { Seq = 1, Seat = 0, PokemonId = 2, Faces = new[] { R, C, R } },
                new DiceFacesChanged
                {
                    Seq = 1, Seat = 0, Source = new SourceRef(SourceKind.Status, (int)StatusEffect.Drowsy),
                    Changes = new[] { new FaceChange { Index = 0, Before = R, After = S } },
                },
                new DamageBreakdown { Seq = 1, Seat = 0, TargetId = 3, Final = 3, Steps = new[] { new DamageStep { Kind = DamageStepKind.Dice, Value = 3 } } },
            };
            var entry = new FxPlanner(0).Plan(events)[0];
            var roll = entry.Step(560);
            Assert.AreEqual(new[] { S, C, R }, BattleRoll.FinalFaces(entry).ToArray(), "결과 줄에는 면 바꾸기 뒤의 면이 선다");
            Assert.IsTrue(BattleRoll.Changed(entry, 0));
            Assert.IsFalse(BattleRoll.Changed(entry, 1));

            // 세기: 멈춤(던짐 400 + 굴림 위쪽 끝 1600) + 면 바꾸기 한 박자 730 + 틈 250 = 2980부터 130마다
            int count = BattleRoll.CountMs(entry);
            Assert.AreEqual(PlayTiming.DiceThrowMs.Value + PlayTiming.DiceRollMaxMs.Value + PlayTiming.FaceFlipMs.Value + PlayTiming.DiceCountGapMs.Value, count);
            var faces = BattleRoll.FinalFaces(entry);
            Assert.AreEqual(0, BattleRoll.Counted(count, 3, count - 1));
            Assert.AreEqual(1, BattleRoll.Counted(count, 3, count));
            Assert.AreEqual(2, BattleRoll.Counted(count, 3, count + PlayTiming.DiceCountPerMs.Value));
            Assert.AreEqual(3, BattleRoll.Counted(count, 3, count + 10 * PlayTiming.DiceCountPerMs.Value), "다 센 뒤에는 그대로");
            Assert.AreEqual(0, BattleRoll.Sum(faces, 1), "심볼 0");
            Assert.AreEqual(2, BattleRoll.Sum(faces, 2), "+ 급소 2");
            Assert.AreEqual(3, BattleRoll.Sum(faces, 3), "+ 일반 1 = 피해 계산의 주사위 칸");

            // 굴림판은 굴림 단계가 시작하고 150 뒤에 뜨고 항목이 끝날 때까지 떠 있다
            var seq = new FxSequencer();
            seq.Enqueue(new[] { entry });
            Assert.IsFalse(BattleRoll.Showing(seq));
            seq.Advance(roll.StartMs + BattleBeats.DockSwapStartMs - 1);
            Assert.IsFalse(BattleRoll.Showing(seq), "기술 줄이 아직 보인다");
            seq.Advance(1);
            Assert.IsTrue(BattleRoll.Showing(seq));
            seq.Advance(entry.LengthMs - seq.LocalMs - 1);
            Assert.IsTrue(BattleRoll.Showing(seq), "항목이 끝나기 전까지");
            seq.Advance(1);
            Assert.IsFalse(BattleRoll.Showing(seq), "공격 항목이 끝나면 기술 줄로 돌아온다");

            // 계산 줄: 칸 0 · 150 · 200(총합 앞 50까지), 총합 250(결정 203 — 칸이 더 있어도 총합의 때는 같다)
            Assert.AreEqual(0, BattleRoll.CalcLit(3, -1));
            Assert.AreEqual(1, BattleRoll.CalcLit(3, 0));
            Assert.AreEqual(1, BattleRoll.CalcLit(3, 149));
            Assert.AreEqual(2, BattleRoll.CalcLit(3, 150));
            Assert.AreEqual(3, BattleRoll.CalcLit(3, 250));
            Assert.AreEqual(3, BattleRoll.CalcLit(3, 200));
            Assert.IsFalse(BattleRoll.TotalLit(249));
            Assert.IsTrue(BattleRoll.TotalLit(PlayTiming.DamageCalcMs.Value));
            Assert.AreEqual(5, BattleRoll.CalcLit(5, PlayTiming.DamageCalcMs.Value), "드문 칸이 더 생겨도 총합 전에 다 켜진다");
        }

        [Test]
        public void M4_DS움직임은단계안시각만으로정해진다()
        {
            var events = new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.BattleAction, Auto = AutoKind.Human },
                new OrderDecided { Seq = 1, Seat = 0, First = 1 },
                new MoveUsed { Seq = 1, Seat = 0, Side = 0, PokemonId = 2, MoveKey = 0, TargetId = 3 },
                new TypeMatchup { Seq = 1, Seat = 0, AttackerId = 2, DefenderId = 3, Delta = 2 },
                new HpChanged { Seq = 1, Seat = 0, PokemonId = 3, Before = 8, After = 2, Max = 8 },
                new Fainted { Seq = 1, Seat = 0, PokemonId = 3, Side = 1 },
            };
            var entry = new FxPlanner(0).Plan(events)[0];
            FxStep move = entry.Step(590), match = entry.Step(600), drain = entry.Step(612), faint = entry.Step(650);
            MonPose At(int ms, int pokemon, int side, bool enemy)
            {
                var seq = new FxSequencer();
                seq.Enqueue(new[] { entry });
                seq.Advance(ms);
                return BattlePose.Of(seq, pokemon, side, enemy);
            }

            // 선턴(54행): 먼저 움직이는 쪽의 체력 상자가 600 동안 150마다 오르내린다. 다른 쪽은 그대로
            var order = entry.Step(540);
            Assert.AreEqual(1, At(order.StartMs + 10, 3, 1, true).BoxDy);
            Assert.AreEqual(0, At(order.StartMs + BattleBeats.OrderBobEveryMs + 10, 3, 1, true).BoxDy);
            Assert.AreEqual(1, At(order.StartMs + 2 * BattleBeats.OrderBobEveryMs + 10, 3, 1, true).BoxDy);
            Assert.AreEqual(0, At(order.StartMs + BattleBeats.OrderBobMs + 10, 3, 1, true).BoxDy, "흔들림이 끝나면 제자리");
            Assert.AreEqual(0, At(order.StartMs + 10, 2, 0, false).BoxDy);

            // 기술(59행 — 결정 143 ⑦부터 맞는 순간만): 돌진(상대 쪽) → 제자리. 내 자리(왼쪽 아래)에서 상대 쪽은 +x.
            // 옛 박자(결정 143 전): 당김 −3(150) → 돌진 +6(150) → 제자리 — 당김은 이제 굴림과 함께 준비 동작이 한다(M10_결정143_준비동작…)
            Assert.AreEqual(BattleBeats.LungeHitPx, At(move.StartMs + 10, 2, 0, false).Dx);
            Assert.AreEqual(0, At(move.StartMs + BattleBeats.LungeHitMs + 10, 2, 0, false).Dx);
            Assert.AreEqual(-BattleBeats.LungeHitPx, At(move.StartMs + 10, 2, 0, true).Dx, "상대 자리(오른쪽 위)에서는 반대다");
            Assert.AreEqual(0, At(move.StartMs + 10, 3, 1, true).Dx, "맞는 쪽은 그대로");
            Assert.AreEqual(BattleBeats.LungeHitMs + BattleBeats.LungeHomeMs, move.Ms, "기술 단계 = 돌진 + 제자리(PlayTiming.MoveHitMs)");

            // 상성(60행): 맞은 쪽이 깜빡이고, 효과가 굉장하면 흔들린다. 공격한 쪽은 그대로
            Assert.IsFalse(At(match.StartMs + 10, 3, 1, true).Blink);
            Assert.IsTrue(At(match.StartMs + BattleBeats.BlinkEveryMs + 10, 3, 1, true).Blink);
            Assert.AreNotEqual(0, At(match.StartMs + 10, 3, 1, true).Dx);
            Assert.IsFalse(At(match.StartMs + BattleBeats.BlinkMs + 10, 3, 1, true).Blink, "깜빡임이 끝나면 서 있다");
            Assert.IsFalse(At(match.StartMs + BattleBeats.BlinkEveryMs + 10, 2, 0, false).Blink);

            // 체력(61행): 막대는 앞 값에서 새 값으로(빠르게, 그다음 느리게 — 가운데에서 이미 반을 넘게 깎였다), 단계 밖에서는 움직이지 않는다
            Assert.IsFalse(At(drain.StartMs - 10, 3, 1, true).Draining);
            var mid = At(drain.StartMs + drain.Ms / 2, 3, 1, true);
            Assert.IsTrue(mid.Draining);
            Assert.Less(mid.Bar, 5f);
            Assert.Greater(mid.Bar, 2f);
            Assert.AreEqual(8f, At(drain.StartMs, 3, 1, true).Bar, 1e-4);

            // 기절(65행): 600 동안 내려간다
            Assert.AreEqual(0f, At(faint.StartMs - 10, 3, 1, true).Sink);
            Assert.AreEqual(0.5f, At(faint.StartMs + BattleBeats.FaintSinkMs / 2, 3, 1, true).Sink, 1e-4);
            Assert.AreEqual(1f, At(faint.StartMs + BattleBeats.FaintSinkMs + 10, 3, 1, true).Sink, 1e-4);
            Assert.AreEqual(0f, At(faint.StartMs + 100, 2, 0, false).Sink, "쓰러지지 않은 쪽");

            // 같은 시각이면 같은 값(한 번에 보내도 나눠 보내도)
            var stepped = new FxSequencer();
            stepped.Enqueue(new[] { entry });
            int target = drain.StartMs + 333;
            for (int t = 0; t < target; t += 16) stepped.Advance(Math.Min(16, target - t));
            Assert.AreEqual(At(target, 3, 1, true).Bar, BattlePose.Of(stepped, 3, 1, true).Bar);
        }

        // 결정 143 ⑦의 견본 공격(내 포켓몬 2 → 상대 3, 주사위 4개 · 상성 없음): 고르기 → 굴림 → (창이 없으면) 맞는 순간 → 상성 → 계산 줄 → 체력
        static List<GameEvent> Swing(bool window)
        {
            var faces = new[] { AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.StatusSymbol, AttackDieFace.CriticalHit };
            var events = new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.BattleAction, Auto = AutoKind.Human },
                new MoveSelected { Seq = 1, Seat = 0, Side = 0, MoveKey = 0, Type = PokemonType.Fire, Power = 4 },
                new DiceRolled { Seq = 1, Seat = 0, Side = 0, PokemonId = 2, Kind = DiceKind.Attack, Type = PokemonType.Fire, Faces = faces, Sum = AttackDice.Sum(faces), Power = 4 },
            };
            if (window) return events;   // 굴린 뒤 반응 창(디아루가 · 트레이너 AfterRoll)이 열려 항목이 여기서 끝난다
            events.Add(new MoveUsed { Seq = 1, Seat = 0, Side = 0, PokemonId = 2, MoveKey = 0, Type = PokemonType.Fire, TargetId = 3 });
            events.Add(new TypeMatchup { Seq = 1, Seat = 0, AttackerId = 2, DefenderId = 3, Delta = 0 });
            events.Add(new DamageBreakdown { Seq = 1, Seat = 0, TargetId = 3, Final = 5, Steps = new DamageStep[0] });
            events.Add(new HpChanged { Seq = 1, Seat = 0, PokemonId = 3, Before = 10, After = 5, Max = 10 });
            return events;
        }

        [Test]
        public void M10_결정143_준비동작은굴림과함께시작하고_재굴림창동안당긴자세로기다리고_맞는순간에돌진한다()
        {
            var entry = new FxPlanner(0).Plan(Swing(false))[0];
            FxStep roll = entry.Step(560), move = entry.Step(590), match = entry.Step(600), calc = entry.Step(611);
            Assert.AreEqual(0, roll.StartMs, "기술을 누르면 바로 굴림");
            Assert.AreEqual(roll.EndMs, move.StartMs, "맞는 순간은 주사위가 다 선 뒤");
            MonPose At(FxEntry e, int ms, int pokemon, bool enemy, int winding = -1)
            {
                var seq = new FxSequencer();
                seq.Enqueue(new[] { e });
                seq.Advance(ms);
                return BattlePose.Of(seq, pokemon, enemy ? 1 : 0, enemy, winding);
            }

            // 준비 동작: 굴림과 함께 상대 반대쪽으로 당기고(150), 그 뒤 100마다 1px 더 당겨 떤다 — 굴림이 끝날 때까지(맞는 순간 전까지)
            Assert.AreEqual(0, At(entry, 0, 2, false).Dx);
            Assert.Less(At(entry, 50, 2, false).Dx, 0, "누른 직후부터 당긴다");
            Assert.AreEqual(-BattleBeats.LungeBackPx, At(entry, BattleBeats.PrepInMs + 10, 2, false).Dx);
            Assert.AreEqual(-BattleBeats.LungeBackPx - BattleBeats.ChargePx, At(entry, BattleBeats.PrepInMs + BattleBeats.ChargeEveryMs + 10, 2, false).Dx, "떨림");
            Assert.LessOrEqual(At(entry, roll.EndMs - 10, 2, false).Dx, -BattleBeats.LungeBackPx, "주사위가 다 서도 당긴 채");
            Assert.AreEqual(0, At(entry, 500, 3, true).Dx, "맞을 쪽은 그대로");
            Assert.AreEqual(BattleBeats.LungeHitPx, At(entry, move.StartMs + 10, 2, false).Dx, "맞는 순간: 돌진");
            Assert.AreEqual(0, At(entry, move.StartMs + BattleBeats.LungeHitMs + 10, 2, false).Dx, "제자리");
            Assert.AreEqual(-BattleBeats.LungeHitPx, At(entry, move.StartMs + 10, 2, true).Dx, "상대 자리(오른쪽 위)의 공격은 반대 방향");

            // 결과는 맞는 순간 뒤에만 드러난다: 「○○의 △△!」 기록 줄 · 상성 · 피해 · 체력
            Assert.AreEqual(move.StartMs, FxReveal.At(entry, entry.Events.Find(e => e is MoveUsed)));
            Assert.AreEqual(calc.StartMs + PlayTiming.DamageCalcMs.Value, FxReveal.At(entry, entry.Events.Find(e => e is DamageBreakdown)));
            Assert.AreEqual(0, FxReveal.At(entry, entry.Events.Find(e => e is MoveSelected)), "고른 기술은 굴림과 함께 드러난다(준비 동작의 문장)");

            // 박자(방장 = 화면): 기술 단계 1400 → 300. 누른 때 → 피해 숫자(총합 칸)가 서는 때 · 체력 착지
            int damageAt = FxReveal.At(entry, entry.Events.Find(e => e is DamageBreakdown)), landed = entry.Step(612).EndMs;
            Assert.AreEqual(PlayTiming.MoveHitMs, move.Ms);
            Assert.AreEqual(BattleBeats.LungeHitMs + BattleBeats.LungeHomeMs, PlayTiming.MoveHitMs, "화면의 돌진 + 제자리 = 방장 길이");
            Assert.AreEqual(entry.LengthMs, PlayTiming.SeatMs(Swing(false), new PlayTimingContext(0)), "방장 길이 = 화면 항목 길이");
            Assert.LessOrEqual(landed, entry.LengthMs, "체력 착지는 항목 안(결정 203으로 단계 합이 보기 시간 바닥보다 짧아질 수 있다)");
            const int OldMoveMs = 1400;   // 결정 143 전의 기술 단계
            TestContext.WriteLine($"결정 143 ⑦ 박자(주사위 4개 · 상성 없음): 누름 → 피해 표시 {damageAt + OldMoveMs - PlayTiming.MoveHitMs} → {damageAt} ms · 누름 → 체력 착지 {landed + OldMoveMs - PlayTiming.MoveHitMs} → {landed} ms");
            Assert.AreEqual(roll.Ms + PlayTiming.MoveHitMs + match.Ms + PlayTiming.DamageCalcMs.Value, damageAt);

            // 재굴림 창: 굴린 뒤 창이 열려 항목이 굴림에서 끝난다 — 대기열이 비어도 준비 동작(드러난 고른 기술 = BattleSeen.Winding)이면 당긴 자세로 기다린다
            var rolled = new FxPlanner(0).Plan(Swing(true))[0];
            Assert.IsNull(rolled.Step(590));
            var idle = new FxSequencer();
            idle.Enqueue(new[] { rolled });
            idle.Advance(rolled.LengthMs + 10);
            Assert.IsNull(idle.Current, "창을 기다리는 동안 대기열은 비었다");
            Assert.AreEqual(-BattleBeats.LungeBackPx, BattlePose.Of(idle, 2, 0, false, 2).Dx, "당긴 자세로 멈춰 기다린다");
            Assert.AreEqual(0, BattlePose.Of(idle, 2, 0, false).Dx, "준비 동작이 아니면 제자리");
            Assert.AreEqual(0, BattlePose.Of(idle, 3, 1, true, 2).Dx, "다른 포켓몬은 그대로");
            // 창이 닫힌 뒤 이어지는 항목(재굴림 · 맞는 순간): 맞는 순간 전까지 당긴 자세, 맞는 순간에 돌진
            var rest = new List<GameEvent> { new ActionStarted { Seq = 2, Seat = 0, Kind = ActionKind.BattleAction, Auto = AutoKind.Human, Continues = 1 } };
            rest.AddRange(Swing(false).GetRange(3, 4));
            var after = new FxPlanner(0).Plan(rest)[0];
            var hit = after.Step(590);
            Assert.AreEqual(0, hit.StartMs);
            Assert.AreEqual(BattleBeats.LungeHitPx, At(after, 10, 2, false, 2).Dx, "맞는 순간이 시작하면 돌진(Winding은 MoveUsed가 드러나며 풀린다)");
        }

        [Test]
        public void M10_결정143_드러난고른기술부터맞는순간까지준비동작이다()
        {
            var seen = new BattleSeen();
            seen.Take(new SentIn { Side = 0, PokemonId = 2 }, true);
            seen.Take(new SentIn { Side = 1, PokemonId = 3 }, true);
            Assert.AreEqual(-1, seen.Winding);
            var picked = new MoveSelected { Side = 0, MoveKey = 4 };
            seen.Take(picked, true);
            Assert.AreEqual(2, seen.Winding, "기술을 고르면(드러나면) 준비 동작");
            Assert.AreSame(picked, seen.WindMove);
            seen.Take(new MoveSelected { Side = 0, MoveKey = 9 }, true);
            Assert.AreSame(picked, seen.WindMove, "흉내내기 · 손가락흔들기가 부른 기술은 맞는 순간의 줄이 알린다 — 준비 문장은 처음 고른 기술");
            seen.Take(new MoveUsed { Side = 0, PokemonId = 2, MoveKey = 9, TargetId = 3 }, false);
            Assert.AreEqual(2, seen.Winding, "맞는 순간이 아직 드러나지 않았다(재굴림 창 · 굴림 중)");
            seen.Take(new MoveUsed { Side = 0, PokemonId = 2, MoveKey = 9, TargetId = 3 }, true);
            Assert.AreEqual(-1, seen.Winding, "맞는 순간이 드러나면 끝");
            Assert.IsNull(seen.WindMove);
            // 굴리기 전에 끊긴 공격은 다음 순서 결정 · 쓰러짐 · 배틀 끝에서 풀린다
            seen.Take(new MoveSelected { Side = 1, MoveKey = 1 }, true);
            Assert.AreEqual(3, seen.Winding);
            seen.Take(new OrderDecided { First = 0 }, true);
            Assert.AreEqual(-1, seen.Winding);
            seen.Take(new MoveSelected { Side = 1, MoveKey = 1 }, true);
            seen.Take(new Fainted { PokemonId = 3, Side = 1 }, true);
            Assert.AreEqual(-1, seen.Winding);
        }

        [Test]
        public void M4_결정73_우선권토큰은인트로끝에떨어져놓이고쓰면돌며상대쪽으로간다()
        {
            // 인트로(53행 「도전자 쪽에 우선권 토큰」): 체력 상자가 다 들어온 뒤 도전자(쪽 0) 자리에 떨어져 놓인다 — 그때 드러난다
            var token = new InitiativeTokenAt { Seq = 1, Seat = 0, Side = 0 };
            var intro = new FxPlanner(0).Plan(new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.SendIn, Auto = AutoKind.Human },
                new SentIn { Seq = 1, Seat = 0, Side = 1, PokemonId = 3 },
                new SentIn { Seq = 1, Seat = 0, Side = 0, PokemonId = 2 },
                token,
            })[0];
            var introStep = intro.Step(FxRows.Intro);
            Assert.IsNotNull(introStep);
            Assert.AreEqual(introStep.StartMs + InitiativeBeats.PlaceAtMs, FxReveal.At(intro, token), "토큰은 놓이는 때 드러난다(기둥의 그림도 그때)");
            Assert.Less(InitiativeBeats.PlaceAtMs + InitiativeBeats.DropMs, introStep.Ms, "인트로 박자(3500) 안에서 끝난다 — 방장 길이는 그대로");
            Assert.Greater(InitiativeBeats.PlaceAtMs, EnterBeats.IntroSecondMs + EnterBeats.IntroBoxLeadMs + EnterBeats.IntroBoxMs, "체력 상자가 다 들어온 뒤");
            InitiativePose IntroAt(int ms, int seen)
            {
                var seq = new FxSequencer();
                seq.Enqueue(new[] { intro });
                seq.Advance(ms);
                return InitiativeBeats.Of(seq, seen, false, 0);
            }
            Assert.AreEqual(-1, IntroAt(introStep.StartMs + InitiativeBeats.PlaceAtMs - 10, -1).From, "놓이기 전에는 없다");
            var falling = IntroAt(introStep.StartMs + InitiativeBeats.PlaceAtMs + 10, 0);
            Assert.AreEqual(0, falling.From);
            Assert.AreEqual(0, falling.To);
            Assert.Greater(falling.Lift, 0, "위에서 떨어진다");
            var placed = IntroAt(introStep.StartMs + InitiativeBeats.PlaceAtMs + InitiativeBeats.DropMs + 10, 0);
            Assert.AreEqual(0, placed.Lift, "다 떨어지면 제자리");
            Assert.AreEqual(1f, placed.Width);
            Assert.IsFalse(placed.Back);

            // 넘기기(54행 「쓰면 토큰이 돌며 상대 쪽으로 · 토큰 +600」): 쓴 쪽(0)에서 상대(1)로 돌며 옮겨 간다. 선턴의 상자 흔들림은 그 뒤에
            var pass = new FxPlanner(0).Plan(new List<GameEvent>
            {
                new ActionStarted { Seq = 2, Seat = 0, Kind = ActionKind.Reaction, Auto = AutoKind.Human },
                new OrderDecided { Seq = 2, Seat = 0, First = 0, TokenUsed = true, TokenTo = 1 },
            })[0];
            var order = pass.Step(540);
            Assert.AreEqual(PlayTiming.RowMs(new OrderDecided(), 540) + InitiativeBeats.PassMs, order.Ms, "토큰을 쓴 선턴은 600 더 길다(방장 길이 그대로)");
            FxSequencer PassAt(int ms)
            {
                var seq = new FxSequencer();
                seq.Enqueue(new[] { pass });
                seq.Advance(ms);
                return seq;
            }
            var mid = InitiativeBeats.Of(PassAt(order.StartMs + InitiativeBeats.PassMs / 2), 1, false, 0);
            Assert.AreEqual(0, mid.From, "쓴 쪽에서");
            Assert.AreEqual(1, mid.To, "상대 쪽으로");
            Assert.That(mid.Travel, Is.GreaterThan(0.2f).And.LessThan(0.8f));
            Assert.Greater(mid.Lift, 0, "포물선");
            bool back = false, thin = false;
            for (int t = 0; t < InitiativeBeats.PassMs; t += 20)
            {
                var p = InitiativeBeats.Of(PassAt(order.StartMs + t), 1, false, 0);
                back |= p.Back;
                thin |= p.Width < 0.5f;
                Assert.That(p.Width, Is.InRange(0f, 1f));
            }
            Assert.IsTrue(back && thin, "돈다(동전처럼 좁아지며 뒷면이 보인다)");
            var landed = InitiativeBeats.Of(PassAt(order.StartMs + InitiativeBeats.PassMs + 10), 1, false, 0);
            Assert.AreEqual(1, landed.From);
            Assert.AreEqual(1, landed.To);
            Assert.AreEqual(1f, landed.Width);
            Assert.AreEqual(0, BattlePose.Of(PassAt(order.StartMs + 10), 2, 0, false).BoxDy, "토큰이 도는 동안 선턴 상자는 그대로");
            Assert.AreEqual(1, BattlePose.Of(PassAt(order.StartMs + InitiativeBeats.PassMs + 10), 2, 0, false).BoxDy, "토큰이 닿은 뒤 선턴 상자가 흔들린다");
            // 같은 시각이면 같은 모습(한 번에 보내도 나눠 보내도)
            var stepped = new FxSequencer();
            stepped.Enqueue(new[] { pass });
            int target = order.StartMs + 333;
            for (int t = 0; t < target; t += 16) stepped.Advance(Math.Min(16, target - t));
            var once = InitiativeBeats.Of(PassAt(target), 1, false, 0);
            var parts = InitiativeBeats.Of(stepped, 1, false, 0);
            Assert.AreEqual(once.Travel, parts.Travel);
            Assert.AreEqual(once.Width, parts.Width);

            // 동률을 묻는 동안(54행 「토큰이 빛나며 S41」): 늘 도는 시각으로 빛나고 반짝이가 둘레를 돈다. 묻지 않으면 빛나지 않는다
            bool lit = false;
            var twinkles = new HashSet<int>();
            for (int ms = 0; ms < InitiativeBeats.GlowEveryMs * 2; ms += 50)
            {
                var g = InitiativeBeats.Of(null, 0, true, ms);
                lit |= g.White > 0f;
                twinkles.Add(g.Twinkle);
                Assert.AreEqual(0, g.From);
            }
            Assert.IsTrue(lit, "빛난다");
            Assert.That(twinkles.Count, Is.GreaterThan(1), "반짝이가 자리를 옮긴다");
            var idle = InitiativeBeats.Of(null, 0, false, 123);
            Assert.AreEqual(0f, idle.White);
            Assert.AreEqual(-1, idle.Twinkle);
            Assert.AreEqual(-1, InitiativeBeats.Of(null, -1, true, 123).From, "토큰이 없으면 아무것도");
        }

        [Test]
        public void M9_신의둘째폼이드러나면_쓰러져빠졌던쪽이무대로돌아온다()
        {
            // 78행: 신의 첫 폼이 쓰러지면(Fainted) 무대에서 빠진다 — 둘째 폼(FormChanged · 천상의 오라)이 드러나면 다시 선다(출전 이벤트는 따로 오지 않는다)
            var seen = new BattleSeen();
            seen.Take(new SentIn { Side = 0, PokemonId = 3 }, true);
            seen.Take(new SentIn { Side = 1, PokemonId = 7 }, true);
            seen.Take(new Fainted { PokemonId = 7, Side = 1 }, true);
            Assert.IsTrue(seen.Out[1], "쓰러지면 체력 상자만 남는다");
            var phase = new FormChanged { Source = SourceRef.Rule(RuleSource.CelestialAura), PokemonId = 7, Before = 1, After = 2, Temporary = true };
            seen.Take(phase, false);
            Assert.IsTrue(seen.Out[1], "둘째 폼 단계가 드러나기 전");
            seen.Take(phase, true);
            Assert.IsFalse(seen.Out[1], "둘째 폼으로 다시 섰다 — 단계가 끝난 뒤에도 도트가 보인다");
            Assert.IsFalse(seen.Out[0]);

            // 다른 폼 바꾸기(79행 — 쓰러진 적 없는 포켓몬의 것)는 무대에서 빠진 쪽을 세우지 않는다
            var other = new BattleSeen();
            other.Take(new SentIn { Side = 0, PokemonId = 3 }, true);
            other.Take(new Fainted { PokemonId = 3, Side = 0 }, true);
            other.Take(new FormChanged { PokemonId = 3, Before = 1, After = 2, Temporary = true }, true);
            Assert.IsTrue(other.Out[0]);
        }
    }
}
