using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan §4 「장소·카드」(04 S23·S25·S34, §8-8 16·25행)의 순수 부분: 공중날기 시간표(<see cref="FlyBeats"/>) · 숨겨진 장소 토큰 시간표(<see cref="SpotBeats"/>) ·
    /// 그 두 행이 화면 대기열에 서는지 · 공중날기의 기록 줄. 엔진이 내는 실제 이벤트로 본다(장면 = 좌석 하나 · 규토리 하나 · 조무래기 없는 마을).
    /// </summary>
    public class PlaceStagingTests
    {
        Ctx ctx;
        StringTable ko;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            ctx = TestData.LoadCtx();
            ko = TestData.LoadStrings("ko");
        }

        [Test]
        public void M4_시간대는라운드마다하나씩지난다()
        {
            // 결정 70 ⑥: 1라운드 낮 → 저녁 → 밤 → 다시 낮. 라운드 번호에서만 셈한다(보기 값 — 같은 라운드는 어느 화면에서나 같은 시간대)
            Assert.AreEqual(DayTime.Day, DayTimes.Of(1));
            Assert.AreEqual(DayTime.Evening, DayTimes.Of(2));
            Assert.AreEqual(DayTime.Night, DayTimes.Of(3));
            Assert.AreEqual(DayTime.Day, DayTimes.Of(4));
            Assert.AreEqual(DayTime.Evening, DayTimes.Of(5));
            for (int r = 1; r < 40; r++) Assert.AreEqual(((int)DayTimes.Of(r) + 1) % DayTimes.Count, (int)DayTimes.Of(r + 1), r + "라운드 다음");
            Assert.AreEqual(DayTime.Day, DayTimes.Of(0), "라운드가 없는 때(셋업·서막)는 낮");
            Assert.AreEqual(DayTime.Day, DayTimes.Of(-1));
            Assert.AreEqual(DayTimes.Count, System.Enum.GetValues(typeof(DayTime)).Length);

            // 그림 이름: 낮은 표 그대로, 저녁·밤은 이름 끝에 꼬리 — 시간대 그림이 없는 것(동굴 등)은 그대로
            Assert.AreEqual("Bg/00_plain.png", DayTimes.Art("Bg/00_plain.png", DayTime.Day, true));
            Assert.AreEqual("Bg/00_plain_evening.png", DayTimes.Art("Bg/00_plain.png", DayTime.Evening, true));
            Assert.AreEqual("Platform/grass_enemy_night.png", DayTimes.Art("Platform/grass_enemy.png", DayTime.Night, true));
            Assert.AreEqual("Bg/09_cave_1.png", DayTimes.Art("Bg/09_cave_1.png", DayTime.Night, false));
            Assert.AreEqual("screen.board.time.evening", DayTimes.TextKey(DayTime.Evening));
        }

        [Test]
        public void M4_공중날기박자는떠오름_호_내려앉음이고양끝은땅위다()
        {
            const int total = 3200;
            Assert.That(PlayTiming.RowMs(new Flew(), FlyBeats.Row), Is.EqualTo(total), "길이는 방장과 같은 값");
            var start = FlyBeats.At(0, total);
            Assert.That(start.T, Is.EqualTo(0f));
            Assert.That(start.Height, Is.EqualTo(0f));
            var lifted = FlyBeats.At(total / FlyBeats.LiftPart, total);
            Assert.That(lifted.T, Is.EqualTo(0f).Within(1e-4f), "떠오르는 동안 제자리");
            Assert.That(lifted.Height, Is.EqualTo(FlyBeats.Hover).Within(1e-4f));
            var mid = FlyBeats.At(total / 2, total);
            Assert.That(mid.T, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(mid.Height, Is.EqualTo(1f).Within(1e-3f), "가운데가 가장 높다");
            var landing = FlyBeats.At(total - total / FlyBeats.LandPart, total);
            Assert.That(landing.T, Is.EqualTo(1f).Within(1e-4f), "내려앉는 동안 도착 칸 위");
            Assert.That(landing.Height, Is.EqualTo(FlyBeats.Hover).Within(1e-3f));
            var end = FlyBeats.At(total, total);
            Assert.That(end.T, Is.EqualTo(1f));
            Assert.That(end.Height, Is.EqualTo(0f));
            // 가는 동안 뒤로 가지 않는다
            float last = 0f;
            for (int ms = 0; ms <= total; ms += 40)
            {
                var p = FlyBeats.At(ms, total);
                Assert.That(p.T, Is.GreaterThanOrEqualTo(last - 1e-5f), ms + "ms");
                Assert.That(p.Height, Is.InRange(0f, 1f + 1e-4f), ms + "ms");
                last = p.T;
            }
        }

        [Test]
        public void M4_숨겨진장소토큰은묻는동안떠있고공개면뒤집히고지나가기면내려앉는다()
        {
            const int total = 700;
            Assert.That(PlayTiming.RowMs(new HiddenSpotRevealed(), SpotBeats.Row), Is.EqualTo(total));
            Assert.That(PlayTiming.RowMs(new HiddenSpotPassed(), SpotBeats.Row), Is.EqualTo(total), "길이는 정체·답과 무관하다(B2)");
            foreach (bool reveal in new[] { true, false })
            {
                var asking = SpotBeats.At(0, total, reveal);
                Assert.That(asking.Lift, Is.EqualTo(1f), "묻는 동안의 높이");
                Assert.That(asking.Flip, Is.EqualTo(0f));
                var end = SpotBeats.At(total, total, reveal);
                Assert.That(end.Lift, Is.EqualTo(0f).Within(1e-4f), "끝에는 내려앉는다");
                Assert.That(end.Flip, Is.EqualTo(reveal ? 1f : 0f), "공개만 뒤집힌다");
            }
            int flip = total * SpotBeats.FlipPermille / 1000;
            var half = SpotBeats.At(flip / 2, total, true);
            Assert.That(half.Flip, Is.EqualTo(0.5f).Within(0.01f), "뒤집기 가운데에서 그림이 바뀐다");
            Assert.That(half.Lift, Is.EqualTo(1f + SpotBeats.Peak).Within(0.01f), "뒤집히는 동안 더 떠오른다");
            Assert.That(SpotBeats.At(flip, total, true).Flip, Is.EqualTo(1f).Within(1e-4f));
            int hold = total * SpotBeats.HoldPermille / 1000;
            Assert.That(SpotBeats.At(hold, total, true).Lift, Is.EqualTo(1f).Within(1e-4f), "앞면으로 머문 뒤에 내려앉는다");
            Assert.That(SpotBeats.At(total * SpotBeats.PassPermille / 1000, total, false).Lift, Is.EqualTo(0f).Within(1e-4f), "지나가기는 바로 내려앉는다");
        }

        [Test]
        public void A5_공중날기는화면대기열의16행에서고기록에한줄남는다()
        {
            var s = Flow.Started(ctx, 2, 17);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.Grunt && t.Def == st.HomeVillage) t.Cell = -1;   // 1막 해방을 흉내(MapTests A5와 같다)
            var fly = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.Fly);
            Assert.That(fly, Is.Not.Null, "조무래기 없는 마을 + 규토리 = 공중날기가 합법(A5)");
            int from = st.Cell, to = ctx.Fixtures.Fixtures[fly.Target].Cell;
            var events = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, fly, events);

            var entries = new FxPlanner(seat, 2).Plan(events.Events);
            FxStep step = null;
            foreach (var entry in entries)
            {
                FxBuilt.KeepSteps(entry, x => x.Row == FlyBeats.Row);
                step = step ?? FlyBeats.StepOf(entry);
            }
            Assert.That(step, Is.Not.Null, "공중날기 단계가 대기열에 선다");
            Assert.That(step.Ms, Is.EqualTo(3200));
            var flight = FlyBeats.Flight(step);
            Assert.That(flight.Seat, Is.EqualTo(seat));
            Assert.That(flight.From, Is.EqualTo(from));
            Assert.That(flight.To, Is.EqualTo(to));

            var log = new BoardLog(ctx);
            log.Feed(events.Events);
            var lines = new List<string>();
            foreach (var line in log.Lines) lines.Add(line.Text(k => ko[k], n => "플레이어 " + (n + 1)));
            string village = ko[ctx.Fixtures.Fixtures[fly.Target].Key];
            Assert.That(lines, Has.Some.EqualTo(KoJosa.Fix("플레이어 " + (seat + 1) + "은(는) " + village + "(으)로 날아갔다")), string.Join(" | ", lines));
        }

        [Test]
        public void A23_숨겨진장소의공개와지나가기는25행에서고칸을싣는다()
        {
            foreach (bool reveal in new[] { true, false })
            {
                var s = Flow.Started(ctx, 2, 33);
                int seat = s.ActiveSeat;
                var spot = s.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && Flow.StepInto(s, ctx, t.Cell) >= 0);
                spot.Def = ctx.Items.Ids.IndexOf(HiddenSpotRules.Campsite); // 공개 뒤에도 그 칸에 남는 야영지 — 결과 5종은 HiddenSpotTests
                s.Seats[seat].Cell = Flow.StepInto(s, ctx, spot.Cell);
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Move, spot.Cell));
                var ask = Flow.Only(s, ChoiceKind.HiddenSpot);
                Assert.That(s.Tokens[ask.Subject], Is.SameAs(spot), "선택의 Subject = 그 칸의 토큰 색인");
                // 좌석 손님의 보기에서도 같은 색인의 토큰이 그 칸에 있다(화면이 묻는 동안 띄울 토큰을 이것으로 찾는다)
                var view = Engine.ViewFor(s, seat);
                Assert.That(view.Tokens[ask.Subject].Cell, Is.EqualTo(spot.Cell));
                Assert.That(view.Tokens[ask.Subject].FaceUp, Is.False);

                var events = new EventLog();
                Flow.Answer(s, ctx, ChoiceKind.HiddenSpot, ChoiceAnswer.Of(reveal ? AnswerKind.Accept : AnswerKind.Decline), events);
                FxStep step = null;
                foreach (var entry in new FxPlanner(seat, 2).Plan(events.Events))
                {
                    FxBuilt.KeepSteps(entry, x => x.Row == SpotBeats.Row);
                    step = step ?? SpotBeats.StepOf(entry);
                }
                Assert.That(step, Is.Not.Null, "공개·지나가기 단계가 대기열에 선다");
                Assert.That(step.Ms, Is.EqualTo(700));
                Assert.That(SpotBeats.Cell(step, out bool revealed), Is.EqualTo(spot.Cell));
                Assert.That(revealed, Is.EqualTo(reveal));
                Assert.That(spot.FaceUp, Is.EqualTo(reveal));
                Assert.That(Flow.Only(s, ChoiceKind.MapAction), Is.Not.Null, "어느 쪽이든 턴은 이어진다");
            }
        }

        // 실제 판(실제 진행 Defeat — 약한 나무 → 격파 → 고향에서 [전체 회복] → 조무래기에게 진다 → 귀환 → 다음 내 턴 귀환 회복)을 화면과 같은 길로 짠다:
        // 짜기 → 그리는 단계만(FxBuilt.Drawn · Draws) → 귀환 떼기 → 소리 단서. 갱신마다 (떼기 전 길이 합, 뗀 뒤 항목들)
        List<(int Before, List<FxEntry> Entries)> DefeatUpdates(int maxAnswers)
        {
            var trail = Sinnoh.Ai.SoloTrail.Defeat;
            var pilot = trail.Pilot();
            var session = SoloGame.Start(ctx, new SoloSettings(), trail.Seed, Sinnoh.Ai.SoloTrail.AiSeed, new Sinnoh.Ai.PrincipleAgent(), 0);
            var client = new SeatClient(session.Link, ctx, SoloGame.Seat);
            var planner = new FxPlanner(SoloGame.Seat, SoloGame.SeatCount);
            var updates = new List<(int, List<FxEntry>)>();
            client.Updated += up =>
            {
                pilot.Saw(up.Events);
                var planned = planner.Plan(up.Events);
                FxBuilt.KeepSteps(planned, st => FxBuilt.Draws(st, SoloGame.SeatCount));
                int before = 0;
                foreach (var e in planned) before += e.LengthMs;
                WhiteoutBeats.Split(planned);
                SoundCues.Add(planned, x => x.Seat == SoloGame.Seat || x.Seat < 0);
                updates.Add((before, planned));
            };
            long now = 0;
            for (int i = 0, answers = 0; i < 20000 && answers < maxAnswers; i++)
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
                Assert.That(a, Is.Not.Null, ch.Kind.ToString());
                Assert.That(client.Answer(ch, a, now).Accepted, Is.True, ch.Kind.ToString());
                answers++;
            }
            client.Close();
            return updates;
        }

        [Test]
        public void M4_회복은파티칸이하나씩반짝이고체력은징글끝에착지한다()
        {
            Assert.That(PlayTiming.RowMs(new Healed(), HealBeats.Row), Is.EqualTo(2800), "길이는 방장과 같은 값(회복 징글)");
            Assert.That(FxBuilt.Drawn(HealBeats.Row), Is.True);
            // 반짝임: 첫 칸은 0, 마지막 칸은 길이의 60%, 칸마다 320 동안 1 → 0
            Assert.That(HealBeats.FlashAt(2800, 0, 3), Is.EqualTo(0));
            Assert.That(HealBeats.FlashAt(2800, 1, 3), Is.EqualTo(840));
            Assert.That(HealBeats.FlashAt(2800, 2, 3), Is.EqualTo(1680));
            Assert.That(HealBeats.Flash(840, 2800, 1, 3), Is.EqualTo(1f));
            Assert.That(HealBeats.Flash(839, 2800, 1, 3), Is.EqualTo(0f));
            Assert.That(HealBeats.Flash(840 + HealBeats.FlashMs, 2800, 1, 3), Is.EqualTo(0f));

            var village = new List<FxStep>();
            var respawn = new List<FxStep>();
            foreach (var up in DefeatUpdates(120))
                foreach (var entry in up.Entries)
                {
                    var step = HealBeats.StepOf(entry);
                    if (step == null) continue;
                    var heal = HealBeats.Healing(step);
                    (heal.Source.Id == (int)RuleSource.Respawn ? respawn : village).Add(step);
                    Assert.That(step.Ms, Is.EqualTo(2800));
                    // 회복 뒤에는 그 명령 끝에 받은 여행 스탬프(47행 — M5 「스탬프·도전·거래 화면」)만 올 수 있다
                    int stampMs = 0;
                    foreach (var st in entry.Steps) if (st.Row == DealRows.Stamp) stampMs += st.Ms;   // 귀환의 운명 1장으로 서로 다른 운명 셋(스탬프 7)이 되는 판이다
                    Assert.That(step.EndMs, Is.EqualTo(entry.LengthMs - stampMs), "회복이 항목의 마지막 단계다(그 뒤에 입력이 열린다)");
                    // 체력은 단계 끝(징글 끝음)에 착지한다 · 소리는 다 찰 때(단계 끝) 한 번만(결정 73 ⑥ — 징글은 M9)
                    foreach (var p in heal.Pokemon)
                    {
                        var cue = entry.Cues.Find(c => !c.IsSound && c.Key.Equals(ShownKey.Hp(p.Id)));
                        Assert.That(cue.AtMs, Is.EqualTo(step.EndMs), "체력 착지 = 회복 단계 끝");
                        Assert.That(cue.After, Is.EqualTo(p.HpAfter));
                    }
                    var sounds = entry.Cues.FindAll(c => c.IsSound && c.Row == HealBeats.Row);
                    Assert.That(sounds.Count, Is.EqualTo(1), "회복 소리는 한 번");
                    Assert.That((sounds[0].AtMs, sounds[0].Sound), Is.EqualTo((step.EndMs, SoundKeys.Center)));
                }
            Assert.That(village, Is.Not.Empty, "고향에서 [전체 회복](17행)");
            Assert.That(respawn, Is.Not.Empty, "귀환 뒤 다음 내 턴의 귀환 회복(8행)");
        }

        [Test]
        public void M4_귀환은배틀끝항목에서떼어결과창뒤에보이고길이합은같다()
        {
            Assert.That(PlayTiming.RowMs(new Whiteout(), WhiteoutBeats.Row), Is.EqualTo(2000), "길이는 방장과 같은 값");
            // 어두워짐 → 다 어두운 동안 고향에 섬 → 걷힘
            Assert.That(WhiteoutBeats.At(0, 2000).Dark, Is.EqualTo(0f));
            Assert.That(WhiteoutBeats.At(700, 2000).Dark, Is.EqualTo(1f));
            Assert.That(WhiteoutBeats.At(999, 2000).Home, Is.False);
            Assert.That(WhiteoutBeats.At(1000, 2000).Home, Is.True);
            Assert.That(WhiteoutBeats.At(1000, 2000).Dark, Is.EqualTo(1f), "말이 고향에 서는 때는 다 어둡다");
            Assert.That(WhiteoutBeats.At(2000, 2000).Dark, Is.EqualTo(0f));

            int seen = 0;
            foreach (var up in DefeatUpdates(60))
            {
                int after = 0;
                foreach (var e in up.Entries) after += e.LengthMs;
                Assert.That(after, Is.EqualTo(up.Before), "떼어도 갱신의 길이 합은 같다(방장 멈춤 — P5)");
                for (int i = 0; i < up.Entries.Count; i++)
                {
                    var entry = up.Entries[i];
                    var step = WhiteoutBeats.StepOf(entry);
                    if (step == null) continue;
                    seen++;
                    Assert.That(entry.Steps.Count, Is.EqualTo(1), "귀환 항목은 귀환 단계 하나");
                    Assert.That((step.StartMs, step.Ms, entry.QueueMs), Is.EqualTo((0, 2000, 2000)));
                    Assert.That(entry.Events[0], Is.InstanceOf<Whiteout>());
                    Assert.That(i, Is.GreaterThan(0));
                    var end = up.Entries[i - 1];
                    Assert.That(end.Seq, Is.EqualTo(entry.Seq), "같은 행동(Seq)");
                    Assert.That(end.Events.Exists(e => e is BattleEnded), Is.True, "바로 앞 = 배틀 끝 항목");
                    Assert.That(end.Step(WhiteoutBeats.Row), Is.Null);
                    Assert.That(end.Steps[end.Steps.Count - 1].Row, Is.EqualTo(700), "배틀 끝 항목은 결과(70행)로 끝난다 — 그 뒤에 결과 창이 뜬다");
                    Assert.That(end.Cues.TrueForAll(c => c.AtMs <= end.LengthMs), Is.True);
                    Assert.That(entry.Cues.FindAll(c => c.IsSound).ConvertAll(c => (c.AtMs, c.Sound)), Is.EqualTo(new[] { (entry.Step(WhiteoutBeats.Row).StartMs, SoundKeys.Fall) }), "암전이 내려오기 시작할 때 하강음 하나(M9 소리 ③ — 옛 무음 [가정])");
                }
            }
            Assert.That(seen, Is.GreaterThan(0), "조무래기에게 져서 귀환한다");
        }

        [Test]
        public void M4_AI자막은사유있는답만그리고사유없는답은덜어낸다()
        {
            Assert.That(PlayTiming.RowMs(new ChoiceAnswered { ByAi = true, Seat = SeatId.Galactic }, AiLineBeats.Row), Is.EqualTo(1000), "길이는 방장과 같은 값");
            AiLineBeats.At(0, 1000, out float slide, out float alpha);
            Assert.That((slide, alpha), Is.EqualTo((-1f, 1f)), "왼쪽 밖에서 들어온다");
            AiLineBeats.At(AiLineBeats.InMs, 1000, out slide, out alpha);
            Assert.That((slide, alpha), Is.EqualTo((0f, 1f)));
            AiLineBeats.At(1000, 1000, out slide, out alpha);
            Assert.That(alpha, Is.EqualTo(0f));
            foreach (AiRule rule in new[] { AiRule.Signature, AiRule.BestValue, AiRule.PlanB })
                Assert.That(ko.Contains(AiLineBeats.RuleKey(rule)), Is.True, rule.ToString());
            Assert.That(ko.Contains("screen.battle.ai.line") && ko.Contains("screen.battle.ai.line.bonus"), Is.True);

            int drawn = 0, cut = 0;
            foreach (var up in DefeatUpdates(60))
                foreach (var entry in up.Entries)
                {
                    bool answered = false, reason = false;
                    foreach (var e in entry.Events)
                        if (e is ChoiceAnswered a && a.ByAi && a.Seat <= SeatId.Galactic)
                        {
                            answered = true;
                            reason = a.Reason != null && a.Reason.Rule != AiRule.None;
                        }
                    if (!answered) continue;
                    var step = AiLineBeats.StepOf(entry);
                    if (reason)
                    {
                        drawn++;
                        Assert.That(step, Is.Not.Null);
                        Assert.That((step.StartMs, step.Ms), Is.EqualTo((0, 1000)), "자막 뒤에 기술 연출");
                        Assert.That(entry.Cues.Exists(c => c.IsSound && c.Row == AiLineBeats.Row && c.AtMs == 0 && c.Sound == SoundKeys.Select), Is.True);
                    }
                    else
                    {
                        cut++;
                        Assert.That(entry.Step(AiLineBeats.Row), Is.Null, "사유가 없는 답(출전 · 응징 옮기기)은 덜어 낸다");
                    }
                }
            Assert.That(drawn, Is.GreaterThan(0), "조무래기의 기술 선택");
            Assert.That(cut, Is.GreaterThan(0), "조무래기의 출전");
        }

        [Test]
        public void M4_실제판의배틀에서맞는불꽃은체력이깎이기시작할때기술타입색으로서고기절먼지와볼열림빛이선다_인트로에는빛이없다()
        {
            int hits = 0, faints = 0, opens = 0, intros = 0;
            var list = new List<FxStamp>();
            foreach (var up in DefeatUpdates(120))
                foreach (var entry in up.Entries)
                    foreach (var step in entry.Steps)
                        foreach (var e in step.Events)
                        {
                            int id = e is HpChanged h && step.Row == 612 && h.After < h.Before ? h.PokemonId : e is Fainted f ? f.PokemonId : e is SentIn s ? s.PokemonId : -1;
                            if (id < 0) continue;
                            int lead = e is Fainted ? BattleBeats.FaintSinkMs - SheetBeats.FaintDustLeadMs : e is SentIn si ? FxReveal.At(entry, si) - step.StartMs : 0;
                            var seq = new FxSequencer();
                            seq.Enqueue(new[] { entry });
                            seq.Advance(step.StartMs + lead + 10);
                            if (!ReferenceEquals(seq.Current, entry)) continue;   // 단계 끝에 걸친 것(항목이 그 전에 끝난다)
                            list.Clear();
                            SheetBeats.Battle(seq, id, true, true, 192, 92, list);
                            if (e is SentIn && step.Row == FxRows.Intro)
                            {
                                // 배틀 시작(53행)에는 볼 열림 빛이 없다(결정 70 ⑬ ③)
                                Assert.That(list.Exists(x => x.Key == SheetBeats.Open), Is.False, "인트로의 볼 열림 빛");
                                intros++;
                                continue;
                            }
                            bool byMove = SheetBeats.MoveType(entry, id, out var type);
                            if (e is HpChanged && !byMove)
                            {
                                Assert.That(list.Exists(x => x.Key.StartsWith("hit")), Is.False, "기술로 맞은 것이 아니면(기권 · 규칙 피해) 불꽃이 없다");
                                continue;
                            }
                            string key = e is HpChanged ? SheetBeats.Hit(type, true) : e is Fainted ? SheetBeats.Dust : SheetBeats.Open;
                            var stamp = list.Find(x => x.Key == key && x.Ms == 10);
                            Assert.That(stamp.Key, Is.EqualTo(key), e.GetType().Name + " — 그 순간에서 10ms 뒤");
                            Assert.That((stamp.X, stamp.Y), Is.EqualTo((192, e is Fainted ? 92 : 92 - SheetBeats.BodyAbovePx)));
                            if (e is HpChanged)
                            {
                                hits++;
                                Assert.That(step.Ms, Is.EqualTo(PlayTiming.HpDrainMs.Value), "61행 체력이 깎이는 단계");
                            }
                            else if (e is Fainted)
                            {
                                faints++;
                                // 내 자리: 발 점이 메시지 창에 걸려 먼지를 창 바로 위로 올린다(가로는 그대로)
                                list.Clear();
                                SheetBeats.Battle(seq, id, false, true, 64, 146, list);
                                var mine = list.Find(x => x.Key == SheetBeats.Dust);
                                Assert.That((mine.Key, mine.Ms, mine.X, mine.Y), Is.EqualTo((SheetBeats.Dust, 10, 64, 146 - SheetBeats.PlayerDustLiftPx)));
                            }
                            else opens++;
                            // 볼에서 나오지 않는 쪽(야생)은 빛이 없다 · 다른 포켓몬에는 아무것도 없다
                            list.Clear();
                            SheetBeats.Battle(seq, id, true, false, 192, 92, list);
                            if (e is SentIn) Assert.That(list.Exists(x => x.Key == SheetBeats.Open), Is.False);
                            list.Clear();
                            SheetBeats.Battle(seq, 9999, true, true, 192, 92, list);
                            Assert.That(list, Is.Empty);
                        }
            Assert.That(hits, Is.GreaterThan(0), "기술 맞음");
            Assert.That(faints, Is.GreaterThan(0), "기절");
            Assert.That(opens, Is.GreaterThan(0), "출전");
            Assert.That(intros, Is.GreaterThan(0), "인트로");
        }
    }
}
