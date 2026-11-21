using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M4-3 「연출 층 뼈대」(04 §8-2·§8-5, 02 §5-7·§9): 갱신 → 화면 대기열 항목(단계·층·단서), 조정 값 등록부, 주사위 시간표(04 §8-5 규칙 7),
    /// 방장 멈춤과 화면 대기열의 교차 시험 M4_연출길이방장화면같음(화면은 이벤트를 행으로 따로 분류한다 — <see cref="FxRows"/>).
    /// </summary>
    public class FxPlanTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        static void Report(string line)
        {
            TestContext.WriteLine(line);
            TestContext.Progress.WriteLine(line);
        }

        [Test]
        public void M9_남의정비는정비판보기한단계_내화면은없고같은턴의다음정비는묶는다()
        {
            // 결정 120: 정비(38·40·41행)는 남 화면에만 단계 하나(PlayTiming.MaintMs — 방장과 같은 길이), 정비한 좌석의 화면은 단계 없음 · 같은 턴의 다음 정비는 묶어서 한 번
            List<GameEvent> Maint(int seq, int seat) => new List<GameEvent>
            {
                new ActionStarted { Seq = seq, Seat = seat, Kind = ActionKind.HoldItem, Auto = AutoKind.Human },
                new ItemHeld { Seq = seq, Seat = seat, PokemonId = 4, ItemKey = 2, FromPokemonId = -1 },
            };
            var other = new FxPlanner(0, 2);
            var mine = new FxPlanner(1, 2);
            var timing = new PlayTimingContext(0, 2);
            var first = Maint(1, 1);
            var entry = other.Plan(first)[0];
            var step = entry.Step(WorkRows.Maint);
            Assert.IsNotNull(step, "남 화면: 정비 판 보기 단계");
            Assert.AreEqual((PlayTiming.MaintMs, PlayTiming.MaintMs), (step.Ms, entry.QueueMs));
            Assert.AreEqual(PlayTiming.MaintMs, PlayTiming.SeatMs(first, timing), "방장이 센 그 좌석의 길이와 같다");
            CollectionAssert.AreEqual(new[] { 4 }, WorkRows.MaintChanged(step));
            Assert.AreEqual("maint", OtherActs.Key(step.Row));
            var own = mine.Plan(first)[0];
            Assert.IsNull(own.Step(WorkRows.Maint), "정비한 좌석의 화면은 단계 없음");
            Assert.AreEqual(0, own.QueueMs);
            var again = other.Plan(Maint(2, 1))[0];
            Assert.IsNull(again.Step(WorkRows.Maint), "같은 턴의 다음 정비는 묶는다");
            Assert.AreEqual(0, PlayTiming.SeatMs(Maint(2, 1), timing));
            other.Plan(new List<GameEvent> { new TurnStarted { Seq = 3, Seat = 0 } });
            Assert.IsNotNull(other.Plan(Maint(4, 1))[0].Step(WorkRows.Maint), "다음 턴에는 다시");

            // 사당의 그다음 고르기 결과(뜻밖의 선물의 규토리 · 동등한 교환)도 같은 보기 — 정비가 아니라 같은 턴의 정비 뒤에도 묶지 않는다, 꼬리표는 「축복」
            var shrine = new SourceRef(SourceKind.ShrineEvent, 3);
            var gift = other.Plan(new List<GameEvent>
            {
                new ActionStarted { Seq = 5, Seat = 1, Kind = ActionKind.Reward, Auto = AutoKind.Human },
                new ApricornsGained { Seq = 5, Seat = 1, Source = shrine, Changes = new[] { new ResChange { Seat = 1, Res = ResKind.Apricorn, Key = 0, Before = 1, After = 3 } } },
            })[0].Step(WorkRows.Maint);
            Assert.IsNotNull(gift, "뜻밖의 선물의 규토리: 남 화면에 판 보기(같은 턴의 정비 뒤에도)");
            Assert.IsTrue(WorkRows.Gift(gift));
            Assert.AreEqual("bless", OtherActs.KeyOf(gift));
            var swap = other.Plan(new List<GameEvent>
            {
                new ActionStarted { Seq = 6, Seat = 1, Kind = ActionKind.Reward, Auto = AutoKind.Human },
                new PokemonExchanged { Seq = 6, Seat = 1, Source = shrine, PokemonId = 9, From = 1, To = 2 },
            })[0].Step(WorkRows.Maint);
            CollectionAssert.AreEqual(new[] { 9 }, WorkRows.MaintChanged(swap), "동등한 교환: 바뀐 카드");
            var garden = other.Plan(new List<GameEvent>
            {
                new ActionStarted { Seq = 7, Seat = 1, Kind = ActionKind.UseAbility, Auto = AutoKind.Human },
                new ApricornsGained { Seq = 7, Seat = 1, Source = new SourceRef(SourceKind.TrainerCard, 0), Changes = new ResChange[0] },
            });
            Assert.IsTrue(garden.Count == 0 || garden[0].Step(WorkRows.Maint) == null, "사당 밖의 규토리 받기는 단계 없음");
        }

        // ── 조정 값 등록부 ──

        [Test]
        public void M4_조정값등록부는키기본최소최대범위를지킨다()
        {
            var t = new Tunables();
            var a = t.Define("a.ms", 400, 100, 1000, TunableScope.Host);
            var b = t.Define("b.count", 3, 1, 10, TunableScope.Local);
            Assert.AreEqual(400, a.Value);
            Assert.AreSame(a, t.Find("a.ms"));
            Assert.IsNull(t.Find("없는 키"));
            Assert.Throws<ArgumentException>(() => t.Define("a.ms", 1, 0, 2, TunableScope.Host), "같은 키 두 번");
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Define("c", 5, 6, 9, TunableScope.Host), "기본이 범위 밖");

            Assert.IsTrue(t.Set("a.ms", 5000));
            Assert.AreEqual(1000, a.Value, "최대로 자른다");
            Assert.IsTrue(t.Set("a.ms", -1));
            Assert.AreEqual(100, a.Value, "최소로 자른다");
            Assert.IsFalse(t.Set("없는 키", 1));

            // 방장 → 손님: Host 값만 간다. Local 키가 섞여 와도 내 화면 값은 덮지 않는다
            t.Set("a.ms", 700);
            t.Set("b.count", 9);
            var host = t.Snapshot(TunableScope.Host);
            Assert.AreEqual(1, host.Count);
            Assert.AreEqual(new KeyValuePair<string, int>("a.ms", 700), host[0]);
            var guest = new Tunables();
            var ga = guest.Define("a.ms", 400, 100, 1000, TunableScope.Host);
            var gb = guest.Define("b.count", 3, 1, 10, TunableScope.Local);
            host.Add(new KeyValuePair<string, int>("b.count", 9));
            host.Add(new KeyValuePair<string, int>("모르는 키", 1));
            Assert.AreEqual(2, guest.Apply(host, TunableScope.Host), "넣지 않은 수");
            Assert.AreEqual(700, ga.Value);
            Assert.AreEqual(3, gb.Value);

            t.Reset();
            Assert.AreEqual(400, a.Value);
            Assert.AreEqual(3, b.Value);
        }

        // 턴 종료 → 다음 턴(한 좌석 판이면 다음 라운드의 같은 좌석). gain = 그 턴에 규토리를 주웠다(앞선 갱신에서)
        static List<GameEvent>[] SoloTurn(bool gain)
        {
            var before = new List<GameEvent>();
            if (gain)
            {
                before.Add(new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.TakeApricorns });
                before.Add(new ApricornsTaken { Seq = 1, Seat = 0, Count = 2 });
            }
            var pass = new List<GameEvent>
            {
                new ActionStarted { Seq = 2, Seat = 0, Kind = ActionKind.EndTurn },
                new TurnEnded { Seq = 2, Seat = 0 },
                new ActionStarted { Seq = 3, Seat = SeatId.System, Kind = ActionKind.RoundStart },
                new RoundStarted { Seq = 3, Seat = SeatId.System, Round = 2 },
                new ActionStarted { Seq = 4, Seat = 0, Kind = ActionKind.TurnStart },
                new TurnStarted { Seq = 4, Seat = 0, Round = 2 },
            };
            return new[] { before, pass };
        }

        [Test]
        public void M4_한좌석판의턴넘김은띠500컷신500이고요약은변화가있을때만800_결정57()
        {
            // 결정 57 ③: 솔로(한 좌석 판)의 턴 넘김은 1초 — 띠 500 + 컷신 500. 턴 결과 요약은 그 턴에 변화(주운 규토리)가 있었을 때만 800을 더 보인다
            Assert.AreEqual(500, PlayTiming.SoloHandoffMs.Value);
            Assert.AreEqual(500, PlayTiming.SoloMyTurnMs.Value);
            Assert.AreEqual(1000, PlayTiming.HandoffFor(1) + PlayTiming.MyTurnFor(1), "솔로 1초");
            Assert.AreEqual(2000, PlayTiming.TurnSummaryMs.Value + PlayTiming.HandoffFor(2) + PlayTiming.MyTurnFor(2), "여럿 2초(결정 57 ②)");
            Assert.AreEqual(PlayTiming.HandoffFor(6), PlayTiming.HandoffFor(0), "좌석 수를 모르면 여럿");
            Assert.IsFalse(PlayTiming.ShowsTurnSummary(1, false));
            Assert.IsTrue(PlayTiming.ShowsTurnSummary(1, true));
            Assert.IsTrue(PlayTiming.ShowsTurnSummary(2, false), "여럿은 「변화 없음」도 보인다");

            // 변화 없음: 요약 단계가 없다. 방장(PlayTiming.Items)과 화면(FxPlanner)이 같은 길이
            var quiet = SoloTurn(false);
            var screen = new FxPlanner(0, 1);
            var host = new PlayTimingContext(0, 1);
            var entry = screen.Plan(quiet[1]).Find(e => e.Step(FxRows.Handoff) != null);
            Assert.AreEqual("Band:1011@0+500 Band:1012@500+500 Item:60@1000+1400", Shape(entry));
            Assert.IsNull(entry.Step(FxRows.TurnSummary), "「변화 없음」이면 요약을 건너뛴다");
            Assert.AreEqual(1000 + 1400, PlayTiming.SeatMs(quiet[1], host));
            Assert.AreEqual(1000 + 1400, entry.LengthMs, "방장 = 화면");
            // 부품이 있는 행(띠·컷신)만 남기면 솔로의 넘김은 꼭 1000이다 — 띠만 남은 항목에 보기 시간 1500을 채우지 않는다
            FxBuilt.Keep(entry, row => row == FxRows.TurnSummary || row == FxRows.Handoff || row == FxRows.MyTurn);
            Assert.AreEqual(1000, entry.LengthMs);

            // 변화 있음(앞선 갱신에서 규토리를 주웠다 — 턴은 갱신 여러 개에 걸친다): 요약 800이 앞에 선다
            var gain = SoloTurn(true);
            screen = new FxPlanner(0, 1);
            host = new PlayTimingContext(0, 1);
            screen.Plan(gain[0]);
            PlayTiming.Items(gain[0], host);
            Assert.IsTrue(host.TurnGain);
            entry = screen.Plan(gain[1]).Find(e => e.Step(FxRows.Handoff) != null);
            Assert.AreEqual("Band:1010@0+800 Band:1011@800+500 Band:1012@1300+500 Item:60@1800+1400", Shape(entry));
            Assert.AreEqual(1800 + 1400, PlayTiming.SeatMs(gain[1], host));
            Assert.IsFalse(host.TurnGain, "다음 턴이 시작되면 지운다");
            // 그다음 턴은 다시 변화 없음
            entry = screen.Plan(quiet[1]).Find(e => e.Step(FxRows.Handoff) != null);
            Assert.IsNull(entry.Step(FxRows.TurnSummary));
            Assert.AreEqual(1000 + 1400, PlayTiming.SeatMs(quiet[1], host));

            // 좌석 수는 상태에서 받는다: 한 좌석 판의 보기로 다시 세우면 솔로 길이
            var s = Engine.NewGame(ctx, new RuleOptions(), 1, 5, Campaign.HeartOfSinnoh);
            var fromState = new PlayTimingContext(0);
            fromState.From(s);
            Assert.AreEqual(1, fromState.Seats);
            Assert.AreEqual(1000 + 1400, PlayTiming.SeatMs(quiet[1], fromState));
            var planned = new FxPlanner(0);
            planned.From(s);
            Assert.AreEqual(1000 + 1400, planned.Plan(quiet[1]).Find(e => e.Step(FxRows.Handoff) != null).LengthMs);

            // 게임 첫 턴은 컷신만(솔로 500), 여럿은 변화가 없어도 요약이 선다
            var first = new List<GameEvent> { new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.TurnStart }, new TurnStarted { Seq = 1, Seat = 0, Round = 1 } };
            Assert.AreEqual("Band:1012@0+500 Item:60@500+1400", Shape(new FxPlanner(0, 1).Plan(first)[0]));
            Assert.AreEqual("Band:1010@0+800 Band:1011@800+600 Band:1012@1400+600 Item:60@2000+1400", Shape(new FxPlanner(0, 2).Plan(quiet[1]).Find(e => e.Step(FxRows.Handoff) != null)));

            // 조정 값을 바꾸면 방장·화면이 함께 바뀐다
            Tunables.Shared.Set("turn.soloHandoffMs", 700);
            Assert.AreEqual(700 + 500 + 1400, PlayTiming.SeatMs(quiet[1], new PlayTimingContext(0, 1)));
            Assert.AreEqual(700, new FxPlanner(0, 1).Plan(quiet[1]).Find(e => e.Step(FxRows.Handoff) != null).Step(FxRows.Handoff).Ms);
        }

        [Test]
        public void M4_솔로턴넘김뒤입력은연출시각1300에열린다_결정57()
        {
            // 결정 57 ③ [가정] ④: 솔로의 넘김 1000(띠 500 + 컷신 500) 뒤 클릭 막기 300 — [턴 종료]를 누른 뒤 연출 시각 1300에 입력이 열린다.
            // 화면(GameFlow)과 같은 길: 짠 항목에서 부품 있는 행(GameFlow.BuiltRow — 걷기 110 · 요약 · 띠 · 컷신)만 남겨 대기열에 넣는다
            Assert.AreEqual(300, FxTune.ClickGuardMs.Value);
            var entries = new FxPlanner(0, 1).Plan(SoloTurn(false)[1]);
            FxBuilt.Keep(entries, row => row == 110 || row == FxRows.TurnSummary || row == FxRows.Handoff || row == FxRows.MyTurn);
            var seq = new FxSequencer();
            seq.Enqueue(entries);
            seq.Advance(999);
            Assert.IsFalse(seq.IsEmpty, "999: 아직 내 턴 컷신");
            seq.Advance(1);
            Assert.IsTrue(seq.IsEmpty, "1000: 넘김 끝");
            Assert.IsFalse(seq.InputOpen, "1000: 클릭 막기");
            seq.Advance(299);
            Assert.IsFalse(seq.InputOpen, "1299: 아직 클릭 막기");
            seq.Advance(1);
            Assert.IsTrue(seq.InputOpen, "1300: 입력 열림");
            Assert.AreEqual(1300, seq.NowMs);

            // 여럿의 차례 좌석은 2000 + 300
            entries = new FxPlanner(0, 2).Plan(SoloTurn(false)[1]);
            FxBuilt.Keep(entries, row => row == 110 || row == FxRows.TurnSummary || row == FxRows.Handoff || row == FxRows.MyTurn);
            seq = new FxSequencer();
            seq.Enqueue(entries);
            seq.Advance(2299);
            Assert.IsFalse(seq.InputOpen, "2299");
            seq.Advance(1);
            Assert.IsTrue(seq.InputOpen, "2300");
        }

        [Test]
        public void M4_조정값은방장멈춤값과화면값으로나뉜다()
        {
            // 수치는 두 곳: PlayTiming(Host)과 화면 상수(Local) — 04 §8-5
            Assert.AreEqual(TunableScope.Host, PlayTiming.DiceThrowMs.Scope);
            Assert.AreEqual(TunableScope.Local, FxTune.ClickGuardMs.Scope);
            Assert.AreSame(PlayTiming.HpDrainMs, Tunables.Shared.Find("damage.hpMs"));
            Assert.AreSame(FxTune.ToastMs, Tunables.Shared.Find("fx.toastMs"));
            foreach (var e in Tunables.Shared.Entries)
            {
                Assert.AreEqual(e.Default, e.Value, e.Key + ": 시험이 바꾼 값을 되돌리지 않았다");
                Assert.AreEqual(e.Key.StartsWith("fx.", StringComparison.Ordinal) ? TunableScope.Local : TunableScope.Host, e.Scope, e.Key);
            }
            Assert.AreEqual(10 + 13 + 2, Tunables.Shared.Snapshot(TunableScope.Host).Count, "주사위·피해 시간표(43·56·57·61행) 10 + 목업 박자(11·20·68·101행, 결정 53·54) 13 + 한 좌석 판의 넘김 띠·컷신(결정 57 ③) 2");
        }

        // ── 주사위 시간표(04 §8-5 규칙 7, spikes/dice-roll §4) ──

        [Test]
        public void M4_연출길이표는주사위시간표를따른다()
        {
            var two = new DiceRolled { Faces = new AttackDieFace[2] };
            var five = new DiceRolled { Faces = new AttackDieFace[5] };
            Assert.AreEqual(400 + 1600 + 250 + 130 * 2, PlayTiming.RowMs(two, 560), "56행: 던짐 400 + 굴림 위쪽 끝 1600 + 250 뒤 세기 주사위당 130");
            Assert.AreEqual(400 + 1600 + 250 + 130 * 5, PlayTiming.RowMs(five, 560), "상한 없음(옛 값 min(1800, 600 + 120n))");

            var flipOne = new DiceFacesChanged { Changes = new FaceChange[1] };
            var flipThree = new DiceFacesChanged { Changes = new FaceChange[3] };
            Assert.AreEqual(300 + 150 + 280, PlayTiming.RowMs(flipOne, 570), "57행: 꼬리표·고리 300 + 150 뒤 넘김 280");
            Assert.AreEqual(730, PlayTiming.RowMs(flipThree, 570), "여러 개가 바뀌어도 함께 넘어간다(옛 값 바뀐 수 × 350)");
            Assert.AreEqual(0, PlayTiming.RowMs(new DiceFacesChanged { Changes = new FaceChange[0] }, 570), "바꿀 면이 없으면 넘기지 않는다");

            Assert.AreEqual(150 + 100 + 100 + 300 + 400, PlayTiming.RowMs(new DamageBreakdown { Steps = new DamageStep[7] }, 611), "61행: 계산 줄 350 + 300 뒤 숫자 비행 400(단계 수와 무관)");
            Assert.AreEqual(800, PlayTiming.RowMs(new HpChanged(), 612), "61행: HP 감소 800");

            Assert.AreEqual(600 + 200 + 200, PlayTiming.RerollMs(1), "43행: 글리치 600 + 다시 세기 200 + 데미지 갱신 200");
            Assert.AreEqual(600 * 3 + 400, PlayTiming.RerollMs(3), "여러 개면 주사위당 600");

            // 한 항목: 굴림 → 면 바꾸기 두 번(이벤트마다 한 박자) → 동시 피해 둘(같은 박자) → HP
            var ev = new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.BattleAction },
                two, flipOne, flipThree,
                new DamageBreakdown { Steps = new DamageStep[3] }, new DamageBreakdown { Steps = new DamageStep[2] },
                new HpChanged(), new HpChanged(),
            };
            foreach (var e in ev) e.Seat = 0;
            var item = PlayTiming.Items(ev, new PlayTimingContext(0))[0];
            Assert.AreEqual(2510 + 730 * 2 + 1050 + 800, item.StepsMs);
            Assert.AreEqual(item.StepsMs, item.QueueMs, "보기 시간(3000 + 500 × 2)보다 길다");

            // Host 조정 값을 바꾸면 방장 길이가 따라 바뀐다
            Tunables.Shared.Set("dice.countPerMs", 200);
            Assert.AreEqual(2250 + 200 * 2, PlayTiming.RowMs(two, 560));
        }

        // ── 연출 목업 박자(04 §8-5 규칙 8, 결정 53·54 — Logs/UiMock/r4-fx·r5-fx) ──

        [Test]
        public void M4_연출길이표는목업박자를따른다()
        {
            // 11행 걷기: 예비 150 + 칸당 270 + 끝 착지 200(오르막 한 칸마다 +80)
            Assert.AreEqual(150 + 270 * 2 + 200, PlayTiming.RowMs(new Moved { Path = new int[3] }, 110), "2칸 = 890(r4 보통)");
            Assert.AreEqual(150 + 270 * 3 + 80 * 2 + 200, PlayTiming.RowMs(new Moved { Path = new int[4], Levels = new[] { 0, 4, 4, 8 } }, 110), "오르막 두 번");
            Assert.AreEqual(0, PlayTiming.RowMs(new Moved { Path = new int[1] }, 110), "걸음이 없으면 예비·착지도 없다");
            // 20행 찾아보기
            Assert.AreEqual(1750, PlayTiming.RowMs(new Searched(), 200));

            // 68행 포획: 던짐과 굴림이 함께 시작, 판정 = max(2350, 2480 + 470 × 흔들림 수 — 결정 126 ② 던지기 1.5배, 옛 1650), 전체 = 판정 + 결말(성공 900 · 실패 750)
            Assert.AreEqual(3890, PlayTiming.CaptureVerdictMs(3));
            Assert.AreEqual(3420, PlayTiming.CaptureVerdictMs(2));
            Assert.AreEqual(2950, PlayTiming.CaptureVerdictMs(1));
            Assert.AreEqual(2480, PlayTiming.CaptureVerdictMs(0), "흔들림 없음도 볼이 떨어진 뒤");
            Assert.AreEqual(4790, PlayTiming.RowMs(new CaptureAttempt { Success = true, Shakes = 3 }, 680), "성공");
            Assert.AreEqual(4170, PlayTiming.RowMs(new CaptureAttempt { Shakes = 2 }, 680), "1 모자람");
            Assert.AreEqual(3700, PlayTiming.RowMs(new CaptureAttempt { Shakes = 1 }, 680), "2 모자람");
            Assert.AreEqual(3230, PlayTiming.RowMs(new CaptureAttempt { Shakes = 0 }, 680), "3 이상 모자람");

            // 포획 굴림은 68행 안에 겹친다: 항목에 56행 단계가 없고, 길이는 방장 = 화면. 흔들림 수는 규칙이 싣는 값(성공 3, 실패 max(0, 3 − 모자란 값))
            var fail = new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.BattleAction },
                new CaptureAttempt { Seq = 1, Seat = 0, Faces = new AttackDieFace[4], Sum = 4, CatchRate = 5, Shakes = 2 },
                new FatigueChanged { Seq = 1, Seat = 0, Cause = FatigueCause.Capture },
            };
            var entry = new FxPlanner(0).Plan(fail)[0];
            Assert.AreEqual("Item:680@0+4170", Shape(entry));
            Assert.AreEqual(4170, PlayTiming.Items(fail, new PlayTimingContext(0))[0].StepsMs);
            Assert.AreEqual(3000 + 500 * 4, entry.QueueMs, "실패 뒤는 보기 시간(주사위 4개)까지 멈춘 화면");

            // 101행(결정 57 ② — 합쳐 2000): 요약 800 → 넘김 띠 600 → (차례 좌석만) 내 차례 컷신 600. 한 좌석 판은 아래 M4_한좌석판의턴넘김…
            Assert.AreEqual(800, PlayTiming.TurnSummaryMs.Value);
            Assert.AreEqual(600, PlayTiming.HandoffMs.Value);
            Assert.AreEqual(600, PlayTiming.MyTurnMs.Value);
            var pass = new List<GameEvent>
            {
                new ActionStarted { Seq = 2, Seat = 0, Kind = ActionKind.EndTurn },
                new TurnEnded { Seq = 2, Seat = 0 },
                new ActionStarted { Seq = 3, Seat = SeatId.System, Kind = ActionKind.TurnStart },
                new TurnStarted { Seq = 3, Seat = 1 },
            };
            Assert.AreEqual("Band:1010@0+800 Band:1011@800+600 Band:1012@1400+600 Item:60@2000+1400", Shape(new FxPlanner(1, 2).Plan(pass)[1]), "내 차례");
            Assert.AreEqual("Band:1010@0+800 Band:1011@800+600 Item:60@1400+1400", Shape(new FxPlanner(0, 2).Plan(pass)[1]), "남의 차례");
            Assert.AreEqual(2000 + 1400, PlayTiming.UpdateMs(pass, new[] { new PlayTimingContext(0, 2), new PlayTimingContext(1, 2) }), "방장 멈춤은 가장 긴 좌석(차례 좌석)");
            Assert.AreEqual("Band:1010@0+800 Band:1011@800+600 Band:1012@1400+600 Item:60@2000+1400", Shape(new FxPlanner(1).Plan(pass)[1]), "좌석 수를 모르면 여럿으로 짠다");

            // Host 조정 값을 바꾸면 방장 길이가 따라 바뀐다
            Tunables.Shared.Set("capture.shakeMs", 600);
            Assert.AreEqual(2480 + 600 * 3 + 900, PlayTiming.RowMs(new CaptureAttempt { Success = true, Shakes = 3 }, 680));
        }

        // ── 갱신 → 항목 ──

        static List<GameEvent> BattleScript() => new List<GameEvent>
        {
            new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.EncounterPick, Continues = 0, ReactsTo = -1 },
            new EncounterPicked { Seq = 1, Seat = 0 },
            new BattleStarted { Seq = 1, Seat = 0, Kind = BattleKind.Wild, Sides = new[] { new BattleSideInfo { Owner = 0, Seat = 0 }, new BattleSideInfo { Owner = SeatId.Wild, Seat = SeatId.Wild } } },
            new SentIn { Seq = 1, Seat = 0 },
            new InitiativeTokenAt { Seq = 1, Seat = 0 },
            new ActionStarted { Seq = 2, Seat = 0, Kind = ActionKind.BattleAction },
            new DiceRolled { Seq = 2, Seat = 0, Faces = new[] { AttackDieFace.RegularHit, AttackDieFace.CriticalHit } },
            new DamageBreakdown { Seq = 2, Seat = 0, Steps = new DamageStep[2] },
            new HpChanged { Seq = 2, Seat = 0, PokemonId = 9, Before = 20, After = 13 },
            new Fainted { Seq = 2, Seat = 0 },
            new BattleEnded { Seq = 2, Seat = 0, Changes = new[] { new ResChange { Seat = 0, Res = ResKind.Jp, Key = 0, Before = 0, After = 2 } } },
            new TurnEnded { Seq = 2, Seat = 0 },
            new ActionStarted { Seq = 3, Seat = SeatId.System, Kind = ActionKind.TurnStart },
            new TurnStarted { Seq = 3, Seat = 1 },
        };

        static string Shape(FxEntry e)
        {
            var parts = new List<string>();
            foreach (var s in e.Steps) parts.Add($"{s.Layer}:{s.Row}@{s.StartMs}+{s.Ms}");
            return string.Join(" ", parts);
        }

        [Test]
        public void M4_갱신은Seq마다항목하나로이벤트순서대로놓인다()
        {
            var mine = new FxPlanner(0).Plan(BattleScript());
            Assert.AreEqual(3, mine.Count);

            // 조우 고르기(앞 찾아보기를 이어 받음) → 조우 전환 = 당사자 차단 컷신(①) → 인트로(출전은 인트로 안에서)
            Assert.AreEqual(0, mine[0].Continues);
            Assert.AreEqual(-1, mine[0].ReactsTo);
            Assert.AreEqual("Item:212@0+600 Block:220@600+2000 Item:530@2600+3500", Shape(mine[0]));
            Assert.AreEqual(2, mine[0].Step(530).Events.Count, "출전 + 우선권 토큰");
            Assert.AreEqual(2000, mine[0].BlockMs);
            Assert.AreEqual(4100, mine[0].QueueMs);
            Assert.AreEqual(6100, mine[0].LengthMs);

            // 공격: 굴림 → 계산 줄·숫자 비행 → HP → 기절 → 배틀 끝. 표시 값은 그 단계가 끝나는 때 착지한다
            Assert.AreEqual(-1, mine[1].Continues, "이어짐 없음(미설정 표지도 −1)");
            Assert.AreEqual("Item:560@0+2510 Item:611@2510+1050 Item:612@3560+800 Item:650@4360+1800 Item:700@6160+3500", Shape(mine[1]));
            Assert.AreEqual(9660, mine[1].LengthMs);
            Assert.AreEqual(2, mine[1].Cues.Count);
            Assert.AreEqual(ShownKey.Hp(9), mine[1].Cues[0].Key);
            Assert.AreEqual(4360, mine[1].Cues[0].AtMs, "HP 숫자는 막대가 다 깎인 때");
            Assert.AreEqual(ShownKey.Resource(0, ResKind.Jp, 0), mine[1].Cues[1].Key);
            Assert.AreEqual(9660, mine[1].Cues[1].AtMs);

            // 턴 넘김: ② 띠(요약 → 넘김) 뒤에 ③ 턴 시작 배너
            Assert.AreEqual("Band:1010@0+800 Band:1011@800+600 Item:60@1400+1400", Shape(mine[2]));
            Assert.AreEqual(2800, mine[2].LengthMs);

            // 남의 야생전은 내 대기열에 넣지 않는다: 조우 고르기(보드 위)만 보고, 배틀 항목은 길이 0·단서 없음
            var other = new FxPlanner(1).Plan(BattleScript());
            Assert.AreEqual(3, other.Count, "항목은 Seq마다 있다(기록 줄·알림 칩이 달릴 자리)");
            Assert.AreEqual("Item:212@0+600", Shape(other[0]));
            Assert.AreEqual(PlayTiming.PlainMs, other[0].LengthMs, "보기 시간 1500");
            Assert.AreEqual("", Shape(other[1]));
            Assert.AreEqual(0, other[1].LengthMs);
            Assert.AreEqual(0, other[1].Cues.Count);
            Assert.AreEqual(7, other[1].Events.Count, "이벤트는 그대로 들고 있다(ActionStarted 포함)");
            Assert.AreEqual("Band:1010@0+800 Band:1011@800+600 Band:1012@1400+600 Item:60@2000+1400", Shape(other[2]), "차례 좌석은 띠 뒤에 내 차례 컷신 600(결정 53 ① 순서 · 결정 57 ② 길이)");

            // 같은 배틀이 갤럭시단 배틀이면 범위 밖 좌석도 본다: 차단하지 않는 ③ 단계로
            var galactic = BattleScript();
            ((BattleStarted)galactic[2]).Kind = BattleKind.Galactic;
            var seen = new FxPlanner(1).Plan(galactic);
            Assert.AreEqual("Item:212@0+600 Item:220@600+2500 Item:530@3100+3500", Shape(seen[0]));
            Assert.AreEqual(0, seen[0].BlockMs);
            Assert.AreEqual(2, seen[1].Cues.Count);
        }

        [Test]
        public void M4_차단컷신과게임첫턴과걸음()
        {
            var ev = new List<GameEvent>
            {
                // 막 카드 = 전원 차단 컷신. 1막 규토리 3개(행 없음)는 컷신이 끝날 때 착지한다
                new ActionStarted { Seq = 4, Seat = SeatId.System, Kind = ActionKind.ActReveal, Auto = AutoKind.Auto },
                new ActRevealed { Seq = 4, Seat = SeatId.System },
                new ApricornsGained { Seq = 4, Seat = SeatId.System, Changes = new[] { new ResChange { Seat = 1, Res = ResKind.Apricorn, Key = 0, Before = 0, After = 3 } } },
                // 게임 첫 턴: 넘김 없이 차례 좌석에만 내 차례 컷신
                new ActionStarted { Seq = 5, Seat = SeatId.System, Kind = ActionKind.TurnStart, Auto = AutoKind.Auto },
                new TurnStarted { Seq = 5, Seat = 1 },
                // 걸음은 이벤트마다 단계가 이어진다. 정비(none 행)만 있는 항목은 길이 0
                new ActionStarted { Seq = 6, Seat = 1, Kind = ActionKind.Move },
                new Moved { Seq = 6, Seat = 1, Path = new int[3] },
                new Moved { Seq = 6, Seat = 1, Path = new int[2] },
                new ActionStarted { Seq = 7, Seat = 1, Kind = ActionKind.PartyOrder },
                new PartyReordered { Seq = 7, Seat = 1 },
            };
            var mine = new FxPlanner(1).Plan(ev);
            Assert.AreEqual(4, mine.Count);
            Assert.AreEqual(AutoKind.Auto, mine[0].Auto);
            Assert.AreEqual("Block:40@0+3600", Shape(mine[0]));
            Assert.AreEqual(3600, mine[0].BlockMs);
            Assert.AreEqual(0, mine[0].QueueMs);
            Assert.AreEqual(3600, mine[0].Cues[0].AtMs);
            Assert.AreEqual("Band:1012@0+600 Item:60@600+1400", Shape(mine[1]), "내 차례 컷신 600(결정 53 ① · 57 ②)");
            Assert.AreEqual("Item:110@0+890 Item:110@890+620", Shape(mine[2]), "걷기 = 예비 150 + 칸당 270 + 끝 착지 200(결정 53 ①)");
            Assert.AreEqual(890 + 620, mine[2].LengthMs, "보기 시간 1500보다 길다");
            Assert.AreEqual("", Shape(mine[3]));
            Assert.AreEqual(0, mine[3].LengthMs);

            var other = new FxPlanner(0).Plan(ev);
            Assert.AreEqual("Block:40@0+3600", Shape(other[0]), "전원 차단");
            Assert.AreEqual("Item:60@0+1400", Shape(other[1]), "남의 첫 턴에는 내 차례 컷신이 없다");
        }

        // ── 방장 멈춤 = 화면 대기열 ──

        [Test]
        public void M4_화면분류표는이벤트타입을모두분류()
        {
            int n = 0;
            foreach (var t in typeof(GameEvent).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(GameEvent).IsAssignableFrom(t)) continue;
                var e = (GameEvent)Activator.CreateInstance(t);
                Assert.AreNotEqual(FxRows.Unknown, FxRows.Of(e), $"{t.Name}의 화면 행이 없다");
                n++;
            }
            Assert.Greater(n, 60, "이벤트 타입 수");
            Assert.AreEqual(FxRows.Unknown, FxRows.Of(null));
        }

        static readonly int[] SampleSeats = { 0, 1, SeatId.System, SeatId.Galactic, SeatId.UltraBeast, SeatId.Wild };

        /// <summary>이벤트 견본 하나: 배열 필드는 길이 3(원소는 기본값 — 길이·개수식이 0이 아니게), 나머지는 미설정 그대로.</summary>
        static GameEvent Sample(Type t, int seat, FieldInfo field = null, object value = null)
        {
            var e = (GameEvent)Activator.CreateInstance(t);
            e.Seq = 1;
            e.Seat = seat;
            foreach (var f in t.GetFields())
                if (f.FieldType.IsArray) f.SetValue(e, Array.CreateInstance(f.FieldType.GetElementType(), 3));
            if (field != null) field.SetValue(e, value);
            return e;
        }

        /// <summary>타입 하나의 견본들: 좌석마다 기본 + 열거형·bool 필드를 하나씩 모든 값으로 + 출처 종류마다(분기 조건이 타는 값 — Window·Cause·Kind·ByAi·IsExtra·Source).</summary>
        static List<GameEvent> Samples(Type t)
        {
            var list = new List<GameEvent>();
            foreach (int seat in SampleSeats)
            {
                list.Add(Sample(t, seat));
                foreach (var f in t.GetFields())
                {
                    if (f.FieldType.IsEnum)
                        foreach (var v in Enum.GetValues(f.FieldType)) list.Add(Sample(t, seat, f, v));
                    else if (f.FieldType == typeof(bool)) list.Add(Sample(t, seat, f, true));
                    else if (f.FieldType == typeof(SourceRef))
                        foreach (SourceKind k in Enum.GetValues(typeof(SourceKind)))
                            foreach (int id in new[] { 0, (int)RuleSource.Fatigue })
                                list.Add(Sample(t, seat, f, new SourceRef(k, id)));
                }
            }
            return list;
        }

        static string Name(GameEvent e)
        {
            var parts = new List<string> { "Seat=" + e.Seat, "Source=" + e.Source.Kind + "/" + e.Source.Id };
            foreach (var f in e.GetType().GetFields())
                if (f.FieldType.IsEnum || f.FieldType == typeof(bool)) parts.Add(f.Name + "=" + f.GetValue(e));
            return e.GetType().Name + "(" + string.Join(", ", parts) + ")";
        }

        [Test]
        public void M4_화면행과방장행은타입과분기마다같다()
        {
            // 두 표(FxRows · PlayTiming.RowOf)는 일부러 따로 적었다. 봇 판에 나오지 않는 행·분기(포획·막 목표·규토리 줍기·교체 잠금·반응 창 묶음·배신자 배틀·추가 턴)도
            // 타입 × 분기 값 × 좌석마다 ① 행 숫자를 바로 견주고 ② 한 항목짜리 대본으로 방장 길이와 화면 단계를 견준다(같은 행 두 번 = 더하는 행, 앞뒤에 턴 끝·배틀 시작·우선권 토큰 = 띠·인트로).
            foreach (BattleKind k in Enum.GetValues(typeof(BattleKind)))
                Assert.AreEqual(PlayTiming.IsWildKind(k), FxRows.WildLike(k), $"야생 취급: {k}");

            var layers = new int[6];
            int types = 0, samples = 0, scripts = 0;
            foreach (var t in typeof(GameEvent).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(GameEvent).IsAssignableFrom(t) || t == typeof(ActionStarted)) continue;
                types++;
                foreach (var e in Samples(t))
                {
                    string name = Name(e);
                    Assert.AreEqual(PlayTiming.RowOf(e), FxRows.Of(e), $"{name}: 방장 행 ≠ 화면 행");
                    samples++;

                    var around = new GameEvent[]
                    {
                        new TurnEnded { Seq = 1, Seat = e.Seat },
                        new InitiativeTokenAt { Seq = 1, Seat = e.Seat },
                        new SentIn { Seq = 1, Seat = e.Seat },
                        new GalacticBattle { Seq = 1, Seat = e.Seat },
                        new BattleStarted { Seq = 1, Seat = e.Seat, Kind = BattleKind.Trainer },
                    };
                    var list = new List<List<GameEvent>> { new List<GameEvent> { e }, new List<GameEvent> { e, e } };
                    foreach (var other in around)
                    {
                        list.Add(new List<GameEvent> { other, e });
                        list.Add(new List<GameEvent> { e, other });
                    }
                    foreach (var body in list)
                        for (int viewer = 0; viewer < 2; viewer++)
                        {
                            body.Insert(0, new ActionStarted { Seq = 1, Seat = e.Seat, Kind = ActionKind.BattleAction });
                            Compare(body, new PlayTimingContext(viewer), new FxPlanner(viewer), $"{name} × {body.Count - 1}개({body[1].GetType().Name} 먼저) 좌석 {viewer}", layers);
                            body.RemoveAt(0);
                            scripts++;
                        }
                }
                // 항목 종류(ActionStarted.Kind)를 타는 분기: 첫 출전 고르기의 출전은 인트로가 센다
                foreach (ActionKind kind in Enum.GetValues(typeof(ActionKind)))
                    for (int viewer = 0; viewer < 2; viewer++)
                    {
                        var body = new List<GameEvent> { new ActionStarted { Seq = 1, Seat = 0, Kind = kind }, Sample(t, 0) };
                        Compare(body, new PlayTimingContext(viewer), new FxPlanner(viewer), $"{t.Name} 항목 종류 {kind} 좌석 {viewer}", layers);
                        scripts++;
                    }
            }
            Report($"[M4 방장 행 = 화면 행] 타입 {types}개, 견본 {samples}개, 한 항목 대본 {scripts}개");
            Assert.Greater(types, 60, "이벤트 타입 수");
            Assert.Greater(layers[(int)FxLayer.Block], 0);
            Assert.Greater(layers[(int)FxLayer.Band], 0);
        }

        /// <summary>갱신 하나를 방장 함수와 화면 대기열로 따로 세어 견준다. 돌려주는 값 = 견준 항목 수.</summary>
        static int Compare(IReadOnlyList<GameEvent> events, PlayTimingContext host, FxPlanner screen, string at, int[] layers)
        {
            var items = PlayTiming.Items(events, host);
            var entries = screen.Plan(events);
            int k = 0, total = 0;
            foreach (var entry in entries)
            {
                // 시간축: 단계는 겹치지 않고 이어지며 항목 길이 안에 끝난다. 단서는 길이 안
                int t = 0;
                foreach (var s in entry.Steps)
                {
                    Assert.AreEqual(t, s.StartMs, $"{at} Seq {entry.Seq}: 단계가 이어지지 않는다");
                    t = s.EndMs;
                    layers[(int)s.Layer]++;
                }
                Assert.LessOrEqual(t, entry.LengthMs, $"{at} Seq {entry.Seq}: 단계가 항목 길이를 넘는다");
                foreach (var c in entry.Cues) Assert.LessOrEqual(c.AtMs, entry.LengthMs, $"{at} Seq {entry.Seq}: 단서가 항목 길이 밖");
                // 표시 값은 끊기지 않는다: 같은 키의 단서는 Before = 직전 After(값을 싣는 이벤트가 단서에서 빠지면 여기서 끊긴다 — 독 피해 StatusTicked)
                var landed = new Dictionary<ShownKey, int>();
                foreach (var c in entry.Cues)
                {
                    if (c.Sound != null) continue;
                    if (landed.TryGetValue(c.Key, out int prev)) Assert.AreEqual(prev, c.Before, $"{at} Seq {entry.Seq}: {c.Key}의 표시 값이 끊긴다");
                    landed[c.Key] = c.After;
                }
                total += entry.LengthMs;

                if (entry.Steps.Count == 0)
                {
                    Assert.AreEqual(0, entry.LengthMs, $"{at} Seq {entry.Seq}: 화면에는 단계가 없는데 방장은 길이를 셌다");
                    continue;
                }
                Assert.Less(k, items.Count, $"{at} Seq {entry.Seq}: 화면에는 단계가 있는데 방장 항목이 없다");
                var item = items[k++];
                Assert.AreEqual(item.Seq, entry.Seq, at);
                Assert.AreEqual(item.StepsMs, entry.StepsMs, $"{at} Seq {entry.Seq}: 단계 합(화면이 따로 분류) ≠ 방장 행 길이 합");
                Assert.AreEqual(item.BlockMs, entry.BlockStepsMs, $"{at} Seq {entry.Seq}: 차단 컷신");
                Assert.AreEqual(item.QueueMs, entry.QueueMs, at);
                Assert.AreEqual(item.BlockMs, entry.BlockMs, at);
            }
            Assert.AreEqual(items.Count, k, $"{at}: 방장 항목 수 ≠ 단계가 있는 화면 항목 수");
            int hostMs = 0;
            foreach (var it in items) hostMs += it.QueueMs + it.BlockMs;
            Assert.AreEqual(hostMs, total, $"{at}: 좌석 길이");
            return k;
        }

        [Test, Category("Slow")]
        public void M4_연출길이방장화면같음()
        {
            // 04 §8-1 P5: 방장 멈춤(PlayTiming.Items)과 화면 대기열(FxPlanner — 이벤트를 FxRows로 따로 분류, 배틀 맥락도 따로 든다)이 항목마다 같은 길이를 낸다.
            // 손으로 짠 대본(야생전 당사자·관전, 갤럭시단 배틀, 막 카드, 첫 턴, 걸음)
            var layers = new int[6];
            foreach (var script in new[] { BattleScript(), Galactic(BattleScript()) })
                for (int seat = 0; seat < 2; seat++)
                    Compare(script, new PlayTimingContext(seat), new FxPlanner(seat), "대본 좌석 " + seat, layers);

            // 봇 판 1~6인: 좌석마다 받은 갱신(걸러진 이벤트)을 그대로 양쪽에 흘린다. 맥락은 첫 갱신의 보기에서 세운다(이어하기·재접속처럼)
            int updates = 0, compared = 0, games = 0;
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
                for (ulong seed = 0; seed < PlayoutTests.SessionSeeds; seed++)
                {
                    var agents = new IAgent[seats];
                    for (int i = 0; i < seats; i++) agents[i] = new Playout.BotAgent(seed);
                    var host = new PlayTimingContext[seats];
                    var screen = new FxPlanner[seats];
                    var first = new bool[seats];
                    var session = LocalSession.Start(ctx, new RuleOptions(), seats, seed, Campaign.HeartOfSinnoh,
                        new SessionConfig { VirtualAgent = new PrincipleAgent(), SeatAgents = agents }, 0);
                    for (int i = 0; i < seats; i++)
                    {
                        int seat = i;
                        host[i] = new PlayTimingContext(i);
                        screen[i] = new FxPlanner(i);
                        session.Link.Connect(i, m =>
                        {
                            if (!(m is GameUpdate u)) return;
                            if (!first[seat])
                            {
                                first[seat] = true;
                                host[seat].From(u.View);
                                screen[seat].From(u.View);
                                return;
                            }
                            compared += Compare(u.Events, host[seat], screen[seat], $"{seats}인 시드 {seed} 좌석 {seat} 갱신 {u.Version}", layers);
                            updates++;
                        });
                    }
                    while (session.CommandCount < PlayoutTests.SessionCommands && !session.GameOver)
                    {
                        long t = session.NextDueMs;
                        Assert.AreNotEqual(long.MaxValue, t, $"{seats}인 시드 {seed}: 봇 판이 멈췄다");
                        session.Tick(t);
                    }
                    games++;
                }
            Report($"[M4 방장 = 화면] 봇 판 {games}개(1~6인 × 시드 {PlayoutTests.SessionSeeds}, 판마다 {PlayoutTests.SessionCommands}명령) 좌석 갱신 {updates}개, 견준 항목 {compared}개, " +
                   $"단계 ① {layers[(int)FxLayer.Block]} · ② {layers[(int)FxLayer.Band]} · ③ {layers[(int)FxLayer.Item]}");
            Assert.Greater(compared, 1000, "견준 항목 수");
            Assert.Greater(layers[(int)FxLayer.Block], 0, "차단 컷신이 든 항목");
            Assert.Greater(layers[(int)FxLayer.Band], 0, "넘김 띠가 든 항목");
        }

        [Test]
        public void M7_관전야생전_방판화면은남의야생전도끝까지그리고_길이는방장셈과같다_기본은그대로뺀다()
        {
            // 결정 112 Q19: 방 판의 화면 대기열(WatchWild)은 남의 야생전을 빼지 않는다 — 방장 멈춤(좌석마다 센 것의 최댓값 = 당사자)과 거의 같은 길이로 끝나 기다림이 준다.
            // 기본(방장 셈 · 솔로 · 튜토리얼)은 그대로 뺀다
            var layers = new int[6];
            int Len(List<FxItem> items)
            {
                int ms = 0;
                foreach (var it in items) ms += it.QueueMs + it.BlockMs;
                return ms;
            }
            int skipped = Len(PlayTiming.Items(BattleScript(), new PlayTimingContext(1)));
            int watched = Len(PlayTiming.Items(BattleScript(), new PlayTimingContext(1) { WatchWild = true }));
            int party = Len(PlayTiming.Items(BattleScript(), new PlayTimingContext(0)));
            Assert.Greater(watched, skipped, "남의 야생전을 세면 길어진다");
            Compare(BattleScript(), new PlayTimingContext(1) { WatchWild = true }, new FxPlanner(1) { WatchWild = true }, "관전 좌석 1(방 판)", layers);
            var seen = new FxPlanner(1) { WatchWild = true }.Plan(BattleScript());
            bool battleRows = false;
            foreach (var e in seen) foreach (var s in e.Steps) if (s.Events.Exists(x => x is DiceRolled || x is HpChanged)) battleRows = true;
            Assert.IsTrue(battleRows, "남의 야생전 안(굴림 · 체력)도 그린다");
            var skip = new FxPlanner(1).Plan(BattleScript());
            foreach (var e in skip) foreach (var s in e.Steps) Assert.IsFalse(s.Events.Exists(x => x is DiceRolled || x is HpChanged), "기본은 남의 야생전을 뺀다");
            // 기다림이 준다: 전에는 남의 야생전 전체가 빠져 당사자(= 방장 멈춤)보다 크게 짧았다 → 이제 차이는 당사자만 받는 몫(차단 컷신 · 다음 턴 좌석의 내 차례 컷신)만큼
            Assert.Greater(party - skipped, 5000, "빼던 몫이 컸다");
            Assert.LessOrEqual(Math.Abs(party - watched), 2000, $"관전 좌석 {watched} · 당사자 {party}");
        }

        static List<GameEvent> Galactic(List<GameEvent> script)
        {
            ((BattleStarted)script[2]).Kind = BattleKind.Galactic;
            return script;
        }
    }
}
