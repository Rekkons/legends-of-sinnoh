using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「제작·은행·진화 화면」(04 §8-8 18·36·37·39행)의 순수 부분: 방장과 화면 대기열이 같은 행·길이로 세는지(<see cref="PlayTiming"/> 180·360·370·390 · <see cref="FxRows"/>) ·
    /// 박자(<see cref="CraftBeats"/>·<see cref="StewBeats"/>·<see cref="BankBeats"/>·<see cref="EvolveBeats"/>) · 소리 단서. 엔진이 내는 실제 이벤트로 본다(장면 = 두 좌석 판의 첫 턴 · 손본 규토리·여행 점수).
    /// </summary>
    public class WorkStagingTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        GameState Game()
        {
            var s = Flow.Started(ctx, 2, 32);
            var st = s.Seats[s.ActiveSeat];
            for (int c = 0; c < st.Apricorns.Length; c++) st.Apricorns[c] = 3;
            st.Jp = 20;
            return s;
        }

        List<GameEvent> Do(GameState s, ChoiceKind kind, ChoiceAnswer a)
        {
            var log = new EventLog();
            Flow.Answer(s, ctx, kind, a, log);
            return log.Events;
        }

        // 그 좌석의 화면 대기열(방장과 같은 길이)로 짠 항목들
        static List<FxEntry> Plan(List<GameEvent> events, int viewer, int seats = 2) => new FxPlanner(viewer, seats).Plan(events);

        static FxStep Step(List<FxEntry> entries, int row)
        {
            foreach (var e in entries)
            {
                var s = e.Step(row);
                if (s != null) return s;
            }
            return null;
        }

        [Test]
        public void M5_제작은행진화행은방장과화면이같은행과길이로센다()
        {
            // 제작(36행) — 몬스터볼
            var s = Game();
            int seat = s.ActiveSeat;
            var craft = Do(s, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Craft, Scene.Item(ctx, "item.poke_ball")));
            var crafted = craft.Find(e => e is Crafted);
            Assert.That(PlayTiming.RowOf(crafted), Is.EqualTo(WorkRows.Craft));
            Assert.That(FxRows.Of(crafted), Is.EqualTo(WorkRows.Craft));
            var mine = Plan(craft, seat);
            Assert.That(Step(mine, CraftBeats.Row).Ms, Is.EqualTo(PlayTiming.BallCraftMs), "볼 제작은 짧다(플레이 시험 T11)");
            Assert.That(mine.Find(e => e.Step(CraftBeats.Row) != null).QueueMs, Is.EqualTo(PlayTiming.Items(craft, new PlayTimingContext(seat, 2)).Find(i => i.Seq == crafted.Seq).QueueMs));

            // 스튜(37행) — 자극적인 스튜: 제작 1000 → 스튜 1500
            var zesty = Do(s, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Craft, Scene.Item(ctx, CraftRules.ZestyStew)));
            Assert.That(PlayTiming.RowOf(zesty.Find(e => e is StewEaten)), Is.EqualTo(WorkRows.Stew));
            var stewEntry = Plan(zesty, seat).Find(e => e.Step(StewBeats.Row) != null);
            Assert.That(stewEntry.Step(CraftBeats.Row).StartMs, Is.EqualTo(0));
            Assert.That(stewEntry.Step(StewBeats.Row).StartMs, Is.EqualTo(PlayTiming.CraftMs), "스튜 효과는 만든 뒤");

            // 은행상회(18행) — 고향 마을(조무래기가 있어도 은행상회는 된다)
            var legal = Flow.MapLegal(s, ctx);
            var trade = legal.Find(a => a.Kind == AnswerKind.Ginkgo);
            Assert.IsNotNull(trade, "규토리 셋씩이면 은행상회 줄 하나는 된다");
            var bank = Do(s, ChoiceKind.MapAction, trade);
            Assert.That(PlayTiming.RowOf(bank.Find(e => e is GinkgoTraded)), Is.EqualTo(WorkRows.Bank));
            Assert.That(Step(Plan(bank, seat), BankBeats.Row).Ms, Is.EqualTo(2500));

            // 진화(39행) — 스타팅 포켓몬: 본인 4800 · 남 2000(방장도 같은 값 — 결정 84 L11)
            int starter = s.Seats[seat].Party[0];
            var evolve = Do(s, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Evolve, starter));
            var evolved = (Evolved)evolve.Find(e => e is Evolved);
            Assert.IsNotNull(evolved);
            Assert.That(PlayTiming.RowOf(evolved), Is.EqualTo(WorkRows.Evolve));
            Assert.That(Step(Plan(evolve, seat), EvolveBeats.Row).Ms, Is.EqualTo(PlayTiming.EvolveMs));
            int other = 1 - seat;
            Assert.That(Step(Plan(evolve, other), EvolveBeats.Row).Ms, Is.EqualTo(PlayTiming.EvolveOthersMs));
            Assert.That(PlayTiming.RowMsFor(evolved, 390, other), Is.EqualTo(2000));
            // 진화형이 적당한 포켓몬이면 트레이너 카드 해금(48행 — M6 「트레이너 카드 해금 연출」)이 같은 항목에 붙는다(남 좌석도 해금은 같은 길이 — #708)
            var unlocked = evolve.Find(e => e is CardUnlocked);
            Assert.That(PlayTiming.Items(evolve, new PlayTimingContext(seat, 2)).Find(i => i.Seq == evolved.Seq).StepsMs, Is.EqualTo(PlayTiming.EvolveMs + (unlocked != null ? PlayTiming.RowMsFor(unlocked, 480, seat) : 0)));
            Assert.That(PlayTiming.Items(evolve, new PlayTimingContext(other, 2)).Find(i => i.Seq == evolved.Seq).StepsMs, Is.EqualTo(PlayTiming.EvolveOthersMs + (unlocked != null ? PlayTiming.RowMsFor(unlocked, 480, seat) : 0)));

            // 네 행 모두 화면이 그리는 행이고 소리 표에 줄이 있다
            foreach (int row in new[] { WorkRows.Bank, WorkRows.Craft, WorkRows.Stew, WorkRows.Evolve })
            {
                Assert.IsTrue(FxBuilt.Drawn(row), row + "행");
                Assert.IsTrue(SoundCues.Covers(row), row + "행 소리 표");
            }
        }

        [Test]
        public void M5_남좌석의공물축복제작은행진화는그좌석칩의꼬리표다()
        {
            // 결정 80 N2 나: 남 좌석의 단계는 꼬리표 · 내 것 · 내가 나눠 준 축복 · 좌석 하나 판 · 그 밖의 행은 아니다
            FxStep Of(int row, params GameEvent[] events)
            {
                var st = new FxStep { Row = row, Ms = 1500 };
                st.Events.AddRange(events);
                return st;
            }
            Assert.AreEqual("offer", OtherActs.Key(ShrineRows.Offer));
            Assert.AreEqual("bless", OtherActs.Key(ShrineRows.Blessing));
            Assert.AreEqual("bank", OtherActs.Key(WorkRows.Bank));
            Assert.AreEqual("craft", OtherActs.Key(WorkRows.Craft));
            Assert.AreEqual("craft", OtherActs.Key(WorkRows.Stew), "스튜 효과는 제작");
            Assert.AreEqual("evolve", OtherActs.Key(WorkRows.Evolve));
            Assert.IsNull(OtherActs.Key(ShrineRows.Tally), "집계는 모두의 사당 판이 그린다");
            Assert.IsNull(OtherActs.Key(ShrineRows.Legend));

            Assert.IsTrue(OtherActs.Shows(Of(WorkRows.Craft, new Crafted { Seat = 1 }), 0, 2), "남 좌석의 제작");
            Assert.IsFalse(OtherActs.Shows(Of(WorkRows.Craft, new Crafted { Seat = 0 }), 0, 2), "내 제작은 내 창이 그린다");
            Assert.IsFalse(OtherActs.Shows(Of(WorkRows.Craft, new Crafted { Seat = 1 }), 0, 1), "좌석 하나 판");
            Assert.IsTrue(OtherActs.Shows(Of(ShrineRows.Blessing, new ShrineBlessing { Seat = 2, From = 1 }), 0, 4), "남이 남에게 나눈 축복");
            Assert.IsFalse(OtherActs.Shows(Of(ShrineRows.Blessing, new ShrineBlessing { Seat = 2, From = 0 }), 0, 4), "내가 나눠 준 축복은 사당 판이 그린다");
            Assert.IsFalse(OtherActs.Shows(Of(ShrineRows.Offer), 0, 2), "이벤트 없는 단계");
            Assert.AreEqual("stamp", OtherActs.Key(DealRows.Stamp), "남의 스탬프 = 「스탬프 n/7」(결정 178)");
            Assert.IsTrue(OtherActs.Shows(Of(DealRows.Stamp, new StampGained { Seat = 1, CountAfter = 3 }), 0, 6), "6인 판 남 좌석의 스탬프");
            Assert.IsFalse(OtherActs.Shows(Of(DealRows.Stamp, new StampGained { Seat = 0, CountAfter = 3 }), 0, 6), "내 스탬프는 도장 덮개가 그린다");

            // 박자: 떠오르며 나타나고 끝에서 흐려진다
            Assert.AreEqual(0f, OtherActs.Alpha(0, 1500, out float rise0));
            Assert.AreEqual(1f, rise0);
            Assert.AreEqual(1f, OtherActs.Alpha(OtherActs.InMs, 1500, out float rise1));
            Assert.AreEqual(0f, rise1);
            Assert.AreEqual(1f, OtherActs.Alpha(700, 1500, out _));
            Assert.Less(OtherActs.Alpha(1500 - OtherActs.OutMs / 2, 1500, out _), 1f);
            Assert.AreEqual(0f, OtherActs.Alpha(1500, 1500, out _));
        }

        [Test]
        public void M5_제작은행진화행의소리단서는박자의그때에선다()
        {
            var s = Game();
            int seat = s.ActiveSeat;
            var craft = Plan(Do(s, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Craft, Scene.Item(ctx, CraftRules.ZestyStew))), seat);
            var entry = craft.Find(e => e.Step(StewBeats.Row) != null);
            SoundCues.Add(entry, true);
            var cues = new List<(int, string)>();
            foreach (var c in entry.Cues) if (c.IsSound && c.Row != DealRows.Stamp) cues.Add((c.AtMs, c.Sound));   // 그 명령 끝에 받은 여행 스탬프(47행)의 소리는 뺀다
            CollectionAssert.AreEqual(new[] { (CraftBeats.FlashAt, SoundKeys.BallOpen), (PlayTiming.CraftMs, SoundKeys.Select) }, cues, "제작의 펑 · 스튜 그릇이 처음 튐");

            var evolve = Plan(Do(s, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Evolve, s.Seats[seat].Party[0])), seat).Find(e => e.Step(EvolveBeats.Row) != null);
            SoundCues.Add(evolve, true);
            var flash = evolve.Cues.Find(c => c.IsSound && c.Sound == SoundKeys.BallOpen);
            Assert.That(flash.AtMs, Is.EqualTo(EvolveBeats.FlashAt(PlayTiming.EvolveMs)));
            // M9 소리 ②: 단계 처음부터 진화 곡 · 흰 빛이 걷힌 때 팡파르
            var song = evolve.Cues.Find(c => c.IsSound);
            Assert.That((song.AtMs, song.Sound), Is.EqualTo((0, SoundKeys.Evolve)));
            Assert.That(evolve.Cues.Find(c => c.IsSound && c.Sound == SoundKeys.Fanfare).AtMs, Is.EqualTo(PlayTiming.EvolveMs * EvolveBeats.ClearPermille / 1000));
        }

        [Test]
        public void M5_진화박자는흰실루엣이점점빠르게오간뒤흰빛에서진화뒤모습이다()
        {
            const int total = PlayTiming.EvolveMs;
            var start = EvolveBeats.At(0, total);
            Assert.IsFalse(start.To);
            Assert.That(start.White, Is.EqualTo(0f), "처음은 제 색");
            Assert.That(start.Message, Is.EqualTo(1), "「…오잉!?」");
            int whiteTo = total * EvolveBeats.WhitePermille / 1000, swapEnd = total * EvolveBeats.SwapEndPermille / 1000, flash = EvolveBeats.FlashAt(total);
            Assert.That(EvolveBeats.At(whiteTo, total).White, Is.EqualTo(1f), "흰 실루엣(결정 75 C2 가)");
            // 오가는 때를 센다: 뒤 절반이 앞 절반보다 많다(점점 빠르다)
            int firstHalf = 0, secondHalf = 0, mid = (whiteTo + swapEnd) / 2;
            bool was = false;
            for (int ms = whiteTo; ms < swapEnd; ms += 10)
            {
                var p = EvolveBeats.At(ms, total);
                Assert.That(p.White, Is.EqualTo(1f), ms + "ms — 오가는 동안 내내 실루엣");
                if (p.To != was)
                {
                    if (ms < mid) firstHalf++;
                    else secondHalf++;
                    was = p.To;
                }
            }
            Assert.Greater(firstHalf, 0);
            Assert.Greater(secondHalf, firstHalf * 2, "점점 빠르게");
            var bright = EvolveBeats.At(flash, total);
            Assert.IsTrue(bright.To);
            Assert.That(bright.Screen, Is.EqualTo(1f), "흰 빛이 가장 밝을 때 진화 뒤 모습");
            Assert.That(bright.Message, Is.EqualTo(2));
            var end = EvolveBeats.At(total - 1, total);
            Assert.IsTrue(end.To);
            Assert.That(end.White, Is.EqualTo(0f), "끝은 진화 뒤 모습의 제 색");
            Assert.That(end.Screen, Is.EqualTo(0f));
            Assert.That(EvolveBeats.At(total, total).Message, Is.EqualTo(2));
            // 남 좌석의 짧은 것(2000)도 같은 모양(천분율)
            Assert.That(EvolveBeats.At(EvolveBeats.FlashAt(PlayTiming.EvolveOthersMs), PlayTiming.EvolveOthersMs).Screen, Is.EqualTo(1f));
        }

        [Test]
        public void M6_진화연출은실루엣8번오가고건너뛰면착지다()
        {
            // 결정 84 L11
            const int total = PlayTiming.EvolveMs;
            Assert.That(total, Is.EqualTo(4800));
            int whiteTo = total * EvolveBeats.WhitePermille / 1000, flash = EvolveBeats.FlashAt(total);
            int changes = 0;
            bool was = false;
            for (int ms = whiteTo; ms <= flash; ms++)
            {
                bool to = EvolveBeats.At(ms, total).To;
                if (to != was) changes++;
                was = to;
            }
            Assert.That(EvolveBeats.Swaps, Is.EqualTo(8));
            Assert.That(changes, Is.EqualTo(EvolveBeats.Swaps - 1), "오가는 동안 모습이 바뀐 수(마지막은 진화 뒤 모습으로 흰 빛에 든다)");
            // 건너뛰기 = 착지: 진화 뒤 모습의 제 색 + 「축하합니다!」, 흰 빛은 다 걷혔다
            int skip = EvolveBeats.SkipAt(total);
            Assert.That(skip, Is.GreaterThan(flash).And.LessThan(total));
            var land = EvolveBeats.At(skip, total);
            Assert.IsTrue(land.To);
            Assert.That(land.Message, Is.EqualTo(2));
            Assert.That(land.White, Is.EqualTo(0f));
            Assert.That(land.Screen, Is.EqualTo(0f));
            Assert.That(land.JpOut, Is.EqualTo(1f));
        }

        [Test]
        public void M6_재생중항목을그시각까지소리없이건너뛴다()
        {
            // FxSequencer.SkipTo — 진화 창의 스페이스 바(결정 84 L11)가 쓴다: 지금 항목 안에서만, 소리 단서는 내지 않고 표시 값은 내려앉힌다
            var seq = new FxSequencer();
            int heard = 0;
            seq.Sound += c => heard++;
            var entry = new FxEntry { Seq = 7, QueueMs = 1000 };
            entry.Steps.Add(new FxStep { Row = EvolveBeats.Row, Layer = FxLayer.Item, Ms = 1000 });
            entry.Cues.Add(new FxCue { AtMs = 500, Sound = SoundKeys.BallOpen, Row = EvolveBeats.Row });
            var next = new FxEntry { Seq = 8, QueueMs = 1000 };
            next.Steps.Add(new FxStep { Row = EvolveBeats.Row, Layer = FxLayer.Item, Ms = 1000 });
            seq.Enqueue(new[] { entry, next });
            seq.Advance(100);
            Assert.That(seq.SkipTo(900), Is.True);
            Assert.That(seq.LocalMs, Is.EqualTo(900));
            Assert.That(ReferenceEquals(seq.Current, entry), Is.True, "다음 항목으로 넘어가지 않는다");
            Assert.That(heard, Is.EqualTo(0), "건너뛰기는 무음(04 §8-4)");
            Assert.That(seq.SkipTo(800), Is.False, "되돌리지 않는다");
            seq.Advance(100);
            Assert.That(ReferenceEquals(seq.Current, entry), Is.False);
        }

        [Test]
        public void M5_제작과은행상회박자는빨려들고튀어나오고뒤집기가운데에서새카드다()
        {
            // 36행: 재료는 80ms씩 어긋나 빨려 들고, 펑 뒤 완성품이 커졌다 돌아오고 「+1」이 뜬다
            Assert.That(CraftBeats.Suck(CraftBeats.SuckFromMs, 0), Is.EqualTo(0f));
            Assert.That(CraftBeats.Suck(CraftBeats.SuckFromMs + CraftBeats.SuckMs, 0), Is.EqualTo(1f));
            Assert.That(CraftBeats.Suck(CraftBeats.SuckFromMs + CraftBeats.SuckMs, 1), Is.LessThan(1f), "둘째 재료는 늦게");
            Assert.That(CraftBeats.Suck(CraftBeats.SuckFromMs + CraftBeats.SuckEveryMs * 5 + CraftBeats.SuckMs, 5), Is.EqualTo(1f));
            Assert.Less(CraftBeats.SuckFromMs + CraftBeats.SuckEveryMs * 5 + CraftBeats.SuckMs, CraftBeats.FlashAt + 1, "재료 여섯(지닌물건)도 펑 전에 다 빨려 든다");
            Assert.That(CraftBeats.Flash(CraftBeats.FlashAt), Is.EqualTo(1f));
            Assert.That(CraftBeats.Pop(CraftBeats.FlashAt + CraftBeats.PopMs / 3), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(CraftBeats.Pop(1500), Is.EqualTo(1f));
            Assert.That(CraftBeats.Plus(1500), Is.EqualTo(1f));
            // 37행: 그릇이 두 번 튀고 꼬리표
            Assert.Greater(StewBeats.Bounce(StewBeats.FirstMs / 2), StewBeats.Bounce(StewBeats.FirstMs + StewBeats.SecondMs / 2));
            Assert.That(StewBeats.Badge(1500), Is.EqualTo(1f));
            Assert.That(StewBeats.Badge(StewBeats.BadgeAt - 1), Is.EqualTo(0f));
            // 18행: 낸 것 → 받은 것 → 순증감 → 흔들림 → 뒤집기(가운데에서 새 카드)
            Assert.That(BankBeats.Give(BankBeats.GiveFromMs + BankBeats.IconMs, 0), Is.EqualTo(1f));
            Assert.That(BankBeats.Get(BankBeats.GetFromMs - 1, 0), Is.EqualTo(0f));
            Assert.That(BankBeats.Get(BankBeats.GetFromMs + BankBeats.IconMs, 0), Is.EqualTo(1f));
            Assert.That(BankBeats.Net(BankBeats.NetAt + 150), Is.EqualTo(1f));
            Assert.That(BankBeats.Flip(BankBeats.FlipAt + BankBeats.FlipMs / 2 - 1, out bool before), Is.LessThan(0.1f));
            Assert.IsFalse(before);
            BankBeats.Flip(BankBeats.FlipAt + BankBeats.FlipMs / 2, out bool after);
            Assert.IsTrue(after, "뒤집기 가운데부터 새 맨 위 카드");
            Assert.That(BankBeats.Flip(2500, out _), Is.EqualTo(1f));
            Assert.LessOrEqual(BankBeats.FlipAt + BankBeats.FlipMs, 2500, "18행 길이 안에서 끝난다");
        }
    }
}
