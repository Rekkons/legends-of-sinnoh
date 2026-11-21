using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「트레이너 카드 해금 연출」(04 §8-8 35·48·49·50행 — CardStaging.cs): 방장·화면의 행과 길이(남 좌석은 짧다) · 해금·바꾸기·능력·날씨 박자가 단계 길이 안에서 도는지 ·
    /// 배틀 안의 해금이 배틀 끝 항목 뒤로 미뤄지는지(결정 84 M5 가 — 결과 창 [확인] 뒤 보드에서) · 소리 단서.
    /// </summary>
    public class CardStagingTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        static CardUnlocked Unlock(int seat, int before, int after, params int[] slots) =>
            new CardUnlocked { Seat = seat, CardKey = 0, TierBefore = before, TierAfter = after, CauseGrade = after == 3 ? EncounterTier.Strong : EncounterTier.Moderate, AbilitySlots = slots };

        [Test]
        public void M9_염력으로고른칸은psychic파랑의첫대상칸만_다른카드와빨강과빈대상은없다()
        {
            int psychic = ctx.Cards.TrainerCardIds.IndexOf("psychic"), kindler = ctx.Cards.TrainerCardIds.IndexOf("kindler");
            FxStep Step(int card, AbilityColor color, params int[] targets) =>
                new FxStep { Row = CardRows.Ability, Ms = PlayTiming.AbilityMs, Events = { new AbilityUsed { Seat = 1, CardKey = card, Ability = color, Targets = targets } } };
            Assert.AreEqual(12, AbilityBeats.ReachCell(Step(psychic, AbilityColor.Blue, 12), ctx), "염력의 떨어진 칸");
            Assert.AreEqual(-1, AbilityBeats.ReachCell(Step(psychic, AbilityColor.Blue), ctx), "대상이 없다");
            Assert.AreEqual(-1, AbilityBeats.ReachCell(Step(psychic, AbilityColor.Red, 12), ctx), "빨강 능력");
            Assert.AreEqual(-1, AbilityBeats.ReachCell(Step(kindler, AbilityColor.Blue, 12), ctx), "다른 카드(장작 — 대상은 토큰)");
            Assert.AreEqual(-1, AbilityBeats.ReachCell(Step(psychic, AbilityColor.Blue, ctx.Board.Count), ctx), "판 밖");
            Assert.AreEqual(-1, AbilityBeats.ReachCell(null, ctx));
        }

        [Test]
        public void M6_트레이너카드행은방장과화면이같고남좌석도같은길이다()
        {
            var silver = Unlock(0, 1, 2, 1);
            var gold2 = Unlock(0, 1, 3, 1, 2);
            var swap = new TrainerCardSwapped { Seat = 0, FromCard = 0, ToCard = 1, Tier = 3 };
            var used = new AbilityUsed { Seat = 0, Ability = AbilityColor.Blue, Targets = new int[0] };
            var weather = new WeatherSet { Seat = 0, Weather = 2, Before = -1, Changes = new ResChange[0] };
            foreach (var (e, row) in new (GameEvent, int)[] { (silver, 480), (swap, 490), (used, 500), (weather, 350) })
            {
                Assert.AreEqual(row, PlayTiming.RowOf(e), e.GetType().Name + " 방장 행");
                Assert.AreEqual(row, FxRows.Of(e), e.GetType().Name + " 화면 행");
                Assert.IsTrue(FxBuilt.Drawn(row) && CardRows.Built(row), row + "행은 화면이 그린다");
                Assert.IsTrue(SoundCues.Covers(row), row + "행 소리 표");
            }
            Assert.AreEqual(PlayTiming.UnlockMs, PlayTiming.RowMsFor(silver, 480, 0), "해금 한 박자(3200)");
            Assert.AreEqual(PlayTiming.UnlockMs + PlayTiming.UnlockStepMs, PlayTiming.RowMsFor(gold2, 480, 0), "바로 골드면 자물쇠 둘 — 04 「골드 두 박자 +800」");
            Assert.AreEqual(PlayTiming.UnlockMs, PlayTiming.RowMsFor(silver, 480, 1), "남 좌석의 해금도 같은 길이 — 큰 덮개가 착지까지(결정 120 ① · #708)");
            Assert.AreEqual(PlayTiming.UnlockMs + PlayTiming.UnlockStepMs, PlayTiming.RowMsFor(gold2, 480, 1));
            Assert.Less(UnlockBeats.LitAt(2), PlayTiming.RowMsFor(gold2, 480, 1) - UnlockBeats.OutMs, "남 좌석도 착지(「○○의 트레이너 카드 골드!」)가 걷히기 전에 보인다");
            Assert.AreEqual(PlayTiming.SwapMs, PlayTiming.RowMsFor(swap, 490, 0));
            Assert.AreEqual(PlayTiming.SwapMs, PlayTiming.RowMsFor(swap, 490, 1));
            Assert.Less(SwapBeats.LitAt, PlayTiming.RowMsFor(swap, 490, 1) - SwapBeats.OutMs, "남 좌석의 바꾸기도 착지가 보인다");
            Assert.AreEqual(PlayTiming.AbilityMs, PlayTiming.RowMsFor(used, 500, 1), "능력 사용은 남 좌석도 같은 길이(꼬리표)");
            Assert.AreEqual(PlayTiming.WeatherMs, PlayTiming.RowMsFor(weather, 350, 1), "날씨는 모두에게 같다(위 띠 배지)");
            Assert.That(PlayTiming.UnlockMs, Is.InRange(2500, 3600), "04 48행 시간 열");
            Assert.That(PlayTiming.SwapMs, Is.InRange(1500, 2200), "04 49행");
            Assert.That(PlayTiming.AbilityMs, Is.InRange(400, 800), "04 50행");
            Assert.That(PlayTiming.WeatherMs, Is.InRange(1500, 2200), "04 35행");
            Assert.AreEqual("unlock", OtherActs.Key(480));
            Assert.AreEqual("unlock_2", OtherActs.KeyOf(Step(480, PlayTiming.UnlockMs, silver)));
            Assert.AreEqual("unlock_3", OtherActs.KeyOf(Step(480, PlayTiming.UnlockMs, gold2)));
            Assert.IsTrue(OtherActs.Shows(Step(490, 2000, swap), 1, 2), "남 좌석의 카드 바꾸기 = 꼬리표");
            Assert.IsFalse(OtherActs.Shows(Step(490, 2000, swap), 0, 2), "내 것은 꼬리표가 아니다(덮개)");
            Assert.IsFalse(OtherActs.Shows(Step(350, 1800, weather), 1, 2), "날씨는 꼬리표가 아니다(배지)");
        }

        static FxStep Step(int row, int ms, GameEvent e, int start = 0)
        {
            var s = new FxStep { Row = row, Layer = FxLayer.Item, Ms = ms, StartMs = start };
            if (e != null) s.Events.Add(e);
            return s;
        }

        [Test]
        public void M6_해금박자는아이콘_흔들림_깨짐_뒤집기_금속광_착지순으로단계안에서돈다()
        {
            int total = PlayTiming.UnlockMs;
            Assert.AreEqual(0f, UnlockBeats.At(-1, total, 1).Show);
            Assert.AreEqual(-1f, UnlockBeats.At(UnlockBeats.IconFromMs - 1, total, 1).Icon, "아이콘은 카드가 뜬 뒤 날아온다");
            Assert.AreEqual(1f, UnlockBeats.At(UnlockBeats.IconLandAt, total, 1).Icon, "아래 띠에 박힘");
            Assert.AreEqual(0f, UnlockBeats.At(UnlockBeats.IconLandAt - 1, total, 1).Shake, "박히기 전에는 흔들리지 않는다");
            Assert.IsTrue(Enumerable.Range(UnlockBeats.IconLandAt, UnlockBeats.BreakAt(0) - UnlockBeats.IconLandAt).Any(t => UnlockBeats.At(t, total, 1).Shake != 0f), "박힌 뒤 자물쇠가 흔들린다");
            var before = UnlockBeats.At(UnlockBeats.BreakAt(0) - 1, total, 1);
            Assert.AreEqual(0, before.Broken);
            Assert.AreEqual(0, before.Face);
            var broke = UnlockBeats.At(UnlockBeats.BreakAt(0), total, 1);
            Assert.AreEqual(1, broke.Broken, "자물쇠가 깨졌다");
            Assert.AreEqual(0f, broke.Shake, "깨진 뒤에는 흔들리지 않는다");
            Assert.AreEqual(0, broke.Face, "뒤집기 앞 절반은 옛 면");
            Assert.AreEqual(0f, UnlockBeats.At(UnlockBeats.BreakAt(0) + UnlockBeats.FlipMs / 2 - 1, total, 1).Flip, 0.02f, "옆으로 선 순간");
            Assert.AreEqual(1, UnlockBeats.At(UnlockBeats.BreakAt(0) + UnlockBeats.FlipMs / 2, total, 1).Face, "한가운데에 다음 등급 면");
            Assert.AreEqual(1f, UnlockBeats.At(UnlockBeats.BreakAt(0) + UnlockBeats.FlipMs, total, 1).Flip, "다 펴졌다");
            Assert.AreEqual(-1f, UnlockBeats.At(UnlockBeats.SheenAt(1) - 1, total, 1).Sheen);
            Assert.That(UnlockBeats.At(UnlockBeats.SheenAt(1) + UnlockBeats.SheenMs / 2, total, 1).Sheen, Is.InRange(0.3f, 0.7f), "금속광이 카드를 지난다");
            Assert.IsFalse(UnlockBeats.At(UnlockBeats.LitAt(1) - 1, total, 1).Lit);
            Assert.IsTrue(UnlockBeats.At(UnlockBeats.LitAt(1), total, 1).Lit, "열린 칸의 금 테두리 · 꼬리표");
            Assert.Less(UnlockBeats.LitAt(1) + UnlockBeats.TagsMs, total - UnlockBeats.OutMs, "착지가 걷히기 전에 다 보인다");
            Assert.AreEqual(0f, UnlockBeats.At(total, total, 1).Show, "단계 끝에 걷힌다");

            // 바로 골드(자물쇠 둘): 둘째 자물쇠는 800 뒤에 깨지고 면이 두 번 바뀐다
            int gold = PlayTiming.RowMs(Unlock(0, 1, 3, 1, 2), 480);
            Assert.AreEqual(2, UnlockBeats.Breaks(Unlock(0, 1, 3, 1, 2)));
            Assert.AreEqual(UnlockBeats.BreakAt(0) + PlayTiming.UnlockStepMs, UnlockBeats.BreakAt(1));
            var mid = UnlockBeats.At(UnlockBeats.BreakAt(1) - 1, gold, 2);
            Assert.AreEqual(1, mid.Broken);
            Assert.AreEqual(1, mid.Face, "실버 면에서 둘째 자물쇠를 기다린다");
            Assert.IsTrue(Enumerable.Range(UnlockBeats.BreakAt(0) + UnlockBeats.FlipMs, UnlockBeats.BreakAt(1) - UnlockBeats.BreakAt(0) - UnlockBeats.FlipMs).Any(t => UnlockBeats.At(t, gold, 2).Shake != 0f), "둘째 자물쇠도 흔들린다");
            Assert.AreEqual(2, UnlockBeats.At(UnlockBeats.BreakAt(1) + UnlockBeats.FlipMs, gold, 2).Face, "골드 면");
            Assert.Less(UnlockBeats.LitAt(2) + UnlockBeats.TagsMs, gold - UnlockBeats.OutMs);
            Assert.AreEqual(1, UnlockBeats.Breaks(Unlock(0, 2, 3, 2)), "실버 → 골드는 빨강 하나");
        }

        [Test]
        public void M6_바꾸기_능력_날씨박자는단계안에서돈다()
        {
            int swap = PlayTiming.SwapMs;
            Assert.AreEqual(0, SwapBeats.At(SwapBeats.FlipAt + SwapBeats.FlipMs / 2 - 1, swap).Face, "옛 카드");
            Assert.AreEqual(1, SwapBeats.At(SwapBeats.FlipAt + SwapBeats.FlipMs / 2, swap).Face, "새 카드");
            Assert.IsTrue(SwapBeats.At(SwapBeats.LitAt, swap).Lit);
            Assert.Less(SwapBeats.LitAt + SwapBeats.TagsMs, swap - SwapBeats.OutMs);
            Assert.AreEqual(0f, SwapBeats.At(swap, swap).Show);

            int ab = PlayTiming.AbilityMs;
            Assert.Less(AbilityBeats.At(0, ab).Rise, 0f, "아래에서 떠오른다");
            Assert.AreEqual(AbilityBeats.BouncePx, AbilityBeats.At(AbilityBeats.BounceAt + AbilityBeats.BounceMs / 2, ab).Rise, 0.01f, "통통(6px)");
            Assert.AreEqual(1f, AbilityBeats.At(AbilityBeats.InMs, ab).Show);
            Assert.Less(AbilityBeats.BounceAt + AbilityBeats.BounceMs, ab - AbilityBeats.OutMs, "걷히기 전에 다 튄다");
            Assert.AreEqual(0f, AbilityBeats.At(ab, ab).Show);

            // 날씨 휠: 처음 날씨에서 두 바퀴 넘게 돌아 고른 날씨에 멈추고 톡
            var set = new WeatherSet { Weather = 3, Before = 0 };
            Assert.AreEqual(WeatherBeats.Turns * WeatherBeats.Kinds + 3, WeatherBeats.Steps(set));
            Assert.AreEqual(0, WeatherBeats.At(0, set).Shown, "처음 날씨에서 돈다");
            Assert.IsTrue(WeatherBeats.At(0, set).Spinning);
            var seen = new HashSet<int>();
            for (int t = 0; t < WeatherBeats.SpinMs; t += 10) seen.Add(WeatherBeats.At(t, set).Shown);
            Assert.AreEqual(4, seen.Count, "날씨 넷을 모두 지난다");
            Assert.AreEqual(3, WeatherBeats.At(WeatherBeats.SpinMs - 1, set).Shown, "돌다가 고른 날씨에 닿는다");
            Assert.IsFalse(WeatherBeats.At(WeatherBeats.SpinMs, set).Spinning);
            Assert.AreEqual(1f + WeatherBeats.PopScale, WeatherBeats.At(WeatherBeats.SpinMs + WeatherBeats.PopMs / 2, set).Scale, 0.01f, "멈추며 톡");
            Assert.AreEqual(3, WeatherBeats.At(PlayTiming.WeatherMs - 1, set).Shown);
            Assert.Less(WeatherBeats.SpinMs + WeatherBeats.PopMs, PlayTiming.WeatherMs);
            Assert.AreEqual(2, WeatherBeats.At(WeatherBeats.SpinMs, new WeatherSet { Weather = 2, Before = -1 }).Shown, "처음 놓을 때(앞 날씨 없음)도 고른 날씨에 멈춘다");
        }

        static FxEntry Entry(int seq, int seat, params FxStep[] steps)
        {
            var e = new FxEntry { Seq = seq, Seat = seat };
            int t = 0;
            foreach (var s in steps)
            {
                s.StartMs = t;
                t += s.Ms;
                e.Steps.Add(s);
                e.Events.AddRange(s.Events);
            }
            e.QueueMs = t;
            return e;
        }

        [Test]
        public void M6_배틀안의해금은배틀끝항목바로뒤의새항목으로미룬다_갱신을넘어도()
        {
            var hold = new UnlockHold();
            var silver = Unlock(0, 1, 2, 1);
            // 갱신 1: 배틀 시작
            var start = Entry(1, 0, Step(220, 2000, new BattleStarted { Kind = BattleKind.Wild }));
            Assert.AreEqual(0, hold.Defer(new List<FxEntry> { start }));
            Assert.IsTrue(hold.InBattle);
            // 갱신 2: 기절 + 해금(배틀 가운데) — 해금 단계를 떼어 둔다(기절 단계는 그대로)
            var faint = Entry(2, 0, Step(650, 1800, new Fainted()), Step(480, PlayTiming.UnlockMs, silver));
            var mid = new List<FxEntry> { faint };
            Assert.AreEqual(0, hold.Defer(mid));
            Assert.IsNull(faint.Step(480), "배틀 가운데의 해금은 그 자리에서 그리지 않는다");
            Assert.IsNotNull(faint.Step(650));
            Assert.AreEqual(1800, faint.QueueMs, "뗀 만큼 항목이 짧아진다");
            Assert.IsFalse(faint.Events.Contains(silver), "옮겨 갈 항목의 이벤트");
            Assert.AreEqual(1, hold.Count);
            // 갱신 3: 배틀 끝 · 턴 넘김 — 배틀 끝 항목 바로 뒤에 해금 항목이 선다(결과 창 [확인] 뒤)
            var ended = Entry(3, 0, Step(700, 3500, new BattleEnded()));
            var next = Entry(4, 1, Step(FxRows.Handoff, 600, new TurnStarted()));
            var last = new List<FxEntry> { ended, next };
            Assert.AreEqual(1, hold.Defer(last));
            Assert.AreEqual(3, last.Count);
            Assert.AreSame(ended, last[0]);
            var made = last[1];
            Assert.AreEqual(3, made.Seq, "같은 행동(배틀 끝)의 Seq");
            Assert.AreEqual(0, made.Seat, "해금한 좌석");
            Assert.AreEqual(480, made.Steps.Single().Row);
            Assert.AreEqual(0, made.Steps[0].StartMs);
            Assert.AreEqual(PlayTiming.UnlockMs, made.QueueMs, "길이는 그대로 옮긴다");
            Assert.Contains(silver, made.Events);
            Assert.AreSame(next, last[2], "턴 넘김은 해금 뒤");
            Assert.IsFalse(hold.InBattle);
            Assert.AreEqual(0, hold.Count);

            // 배틀 밖의 해금(진화 · 사당 교환 …)은 그 자리에서
            var board = Entry(5, 0, Step(390, 4800, new Evolved()), Step(480, PlayTiming.UnlockMs, silver));
            Assert.AreEqual(0, hold.Defer(new List<FxEntry> { board }));
            Assert.IsNotNull(board.Step(480));
            // 한 항목에 배틀 끝과 해금이 같이 오면(포획) 그 뒤로 뗀다
            var caught = Entry(6, 0, Step(680, 3960, new CaptureAttempt()), Step(480, PlayTiming.UnlockMs, silver), Step(700, 3500, new BattleEnded()));
            var one = new List<FxEntry> { caught };
            Assert.AreEqual(1, hold.Defer(one));
            Assert.IsNull(caught.Step(480));
            Assert.AreEqual(480, one[1].Steps.Single().Row);
        }

        [Test]
        public void M6_배틀도중에이어받은판도해금을배틀끝뒤로미룬다()
        {
            // M6 화면 교차 검토 #4: 이어 받은 판은 BattleStarted를 다시 보지 않아 InBattle이 거짓이었다 — GameFlow.Enter가 보기의 배틀로 Resume
            var silver = Unlock(0, 1, 2, 1);
            var hold = new UnlockHold();
            hold.Resume(true);
            var faint = Entry(2, 0, Step(650, 1800, new Fainted()), Step(480, PlayTiming.UnlockMs, silver));
            Assert.AreEqual(0, hold.Defer(new List<FxEntry> { faint }));
            Assert.IsNull(faint.Step(480), "이어 받은 배틀 가운데의 해금도 떼어 둔다");
            var ended = Entry(3, 0, Step(700, 3500, new BattleEnded()));
            var last = new List<FxEntry> { ended };
            Assert.AreEqual(1, hold.Defer(last));
            Assert.AreEqual(480, last[1].Steps.Single().Row, "배틀 끝 바로 뒤");
            // 배틀 밖에서 이어 받았으면 그 자리에서
            hold.Resume(false);
            var board = Entry(4, 0, Step(650, 1800, new Fainted()), Step(480, PlayTiming.UnlockMs, silver));
            Assert.AreEqual(0, hold.Defer(new List<FxEntry> { board }));
            Assert.IsNotNull(board.Step(480));
        }

        [Test]
        public void M6_실제배틀에서적당한포켓몬을쓰러뜨리면해금단계는배틀끝항목뒤에선다()
        {
            var s = Battles.Game(ctx, 2, 7);
            s.Seats[0].TrainerCard = 0;
            s.Seats[0].TrainerCardTier = TrainerRules.Bronze;
            Scene.Give(s, ctx, 0, "445:0");
            int wild = Scene.Spawn(s, ctx, "388:0", SeatId.Wild);
            Battles.SetHp(s, ctx, wild, 1);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild);
            Battles.Drive(s, ctx, log);
            Assert.AreEqual(1, log.OfType<CardUnlocked>().Count, "시험 준비: 실버 해금");
            var entries = new FxPlanner(0, 2).Plan(log.Events);
            int unlockAt = entries.FindIndex(e => e.Step(480) != null);
            Assert.GreaterOrEqual(unlockAt, 0, "해금 단계가 대기열에 선다");
            new UnlockHold().Defer(entries);
            int ended = entries.FindIndex(e => e.Events.Exists(x => x is BattleEnded));
            Assert.GreaterOrEqual(ended, 0);
            Assert.AreEqual(1, entries.Count(e => e.Step(480) != null), "해금 단계는 하나");
            Assert.AreEqual(ended + 1, entries.FindIndex(e => e.Step(480) != null), "배틀 끝 항목 바로 뒤");
            Assert.AreEqual(PlayTiming.UnlockMs, entries[ended + 1].QueueMs);
        }

        [Test]
        public void M6_소리단서_해금은박자마다_남좌석도같은박자()
        {
            var silver = Unlock(0, 1, 2, 1);
            var mine = Entry(1, 0, Step(480, PlayTiming.UnlockMs, silver));
            SoundCues.Add(mine, true);
            var got = mine.Cues.Where(c => c.IsSound).Select(c => (c.AtMs, c.Sound)).ToList();
            CollectionAssert.AreEqual(new[]
            {
                (UnlockBeats.IconLandAt, SoundKeys.Token), (UnlockBeats.BreakAt(0), SoundKeys.Hit), (UnlockBeats.BreakAt(0) + UnlockBeats.FlipMs / 2, SoundKeys.CardFlip), (UnlockBeats.LitAt(1), SoundKeys.Shine),
            }, got);
            var gold = Entry(1, 0, Step(480, PlayTiming.UnlockMs + PlayTiming.UnlockStepMs, Unlock(0, 1, 3, 1, 2)));
            SoundCues.Add(gold, true);
            Assert.AreEqual(2, gold.Cues.Count(c => c.IsSound && c.Sound == SoundKeys.Hit), "자물쇠마다");
            // 남 좌석(결정 120 ① · #709): 큰 덮개가 내 것과 같은 길이로 뜨므로 뒤집기 · 착지 소리도 같은 시각
            var other = Entry(1, 1, Step(480, PlayTiming.UnlockMs, Unlock(1, 1, 2, 1)));
            SoundCues.Add(other, false);
            CollectionAssert.AreEqual(got, other.Cues.Where(c => c.IsSound).Select(c => (c.AtMs, c.Sound)).ToList(), "남 좌석 해금 = 내 것과 같은 박자");
            var swap = new TrainerCardSwapped { Seat = 1, FromCard = 0, ToCard = 1, Tier = 3 };
            var swapMine = Entry(1, 1, Step(490, PlayTiming.SwapMs, swap));
            SoundCues.Add(swapMine, true);
            var swapOther = Entry(1, 1, Step(490, PlayTiming.SwapMs, swap));
            SoundCues.Add(swapOther, false);
            var swapGot = swapOther.Cues.Where(c => c.IsSound).Select(c => (c.AtMs, c.Sound)).ToList();
            CollectionAssert.AreEqual(new[] { (SwapBeats.FlipAt + SwapBeats.FlipMs / 2, SoundKeys.CardFlip), (SwapBeats.LitAt, SoundKeys.BallOpen) }, swapGot, "남 좌석 바꾸기 = 뒤집기 · 착지");
            CollectionAssert.AreEqual(swapMine.Cues.Where(c => c.IsSound).Select(c => (c.AtMs, c.Sound)).ToList(), swapGot);
            // 덮개가 숨을 수 있는 소리 = 남 좌석의 48 · 49행 단서만(내 것 · 다른 행은 늘 낸다)
            Assert.IsTrue(other.Cues.Where(c => c.IsSound).All(c => CardRows.OthersCover(other, c, 0)), "남 좌석 해금 단서");
            Assert.IsTrue(swapOther.Cues.Where(c => c.IsSound).All(c => CardRows.OthersCover(swapOther, c, 0)), "남 좌석 바꾸기 단서");
            Assert.IsFalse(other.Cues.Where(c => c.IsSound).Any(c => CardRows.OthersCover(other, c, 1)), "내 해금은 숨지 않는다");
            Assert.IsFalse(mine.Cues.Where(c => c.IsSound).Any(c => CardRows.OthersCover(mine, c, 0)), "좌석 0이 보는 좌석 0의 해금");
            var weather = Entry(1, 0, Step(350, PlayTiming.WeatherMs, new WeatherSet { Weather = 1, Before = 0, Changes = new ResChange[0] }));
            SoundCues.Add(weather, false);
            CollectionAssert.AreEqual(new[] { (WeatherBeats.SpinMs, SoundKeys.Select) }, weather.Cues.Where(c => c.IsSound).Select(c => (c.AtMs, c.Sound)).ToList(), "날씨는 누구에게나 멈출 때 톡");
            var ability = Entry(1, 0, Step(500, PlayTiming.AbilityMs, new AbilityUsed { Seat = 0, Ability = AbilityColor.Red, Targets = new int[0] }));
            SoundCues.Add(ability, true);
            Assert.AreEqual(1, ability.Cues.Count(c => c.IsSound));
        }
    }
}
