using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「맵 결과·배틀 안 연출」(04 §8-8 23·24·26~30·46·74~81행 — <c>ResultStaging.cs</c>): 결과 이벤트가 그 행의 단계로 서고(방장과 같은 길이), 값은 그 단계 끝에 착지하고,
    /// 박자 함수는 그 시각의 모습을 한 번에 준다(앞 호출에 기대지 않는다). 기록 줄(잠금 이유 다섯 · 폼 바꾸기 · 비스트 부스트)과 신 격파 단계 떼기도 여기서 본다.
    /// </summary>
    public class ResultStagingTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static List<FxEntry> Plan(List<GameEvent> events, int viewer = 0) => new FxPlanner(viewer, 2).Plan(events);

        static FxEntry With(List<FxEntry> entries, int row)
        {
            var e = entries.Find(x => x.Step(row) != null);
            Assert.IsNotNull(e, row + "행 단계가 없다");
            return e;
        }

        [TestCase("hidden_spot.red_apricorn_stash", ResultRows.Stash, 1500)]
        [TestCase("hidden_spot.campsite", ResultRows.Camp, 1000)]
        [TestCase("hidden_spot.fallen_star", ResultRows.Star, 1800)]
        [TestCase("item.alpha_pokemon", ResultRows.Alpha, 2400)]
        [TestCase("item.gracidea_flower", ResultRows.Relic, 1800)]
        public void M5_숨겨진장소결과는공개뒤그행의단계로서고화면이그린다(string key, int row, int ms)
        {
            var events = ResultScenes.Spot(ctx, key);
            int seat = events.Find(e => e is SpotResolved).Seat;
            var entries = Plan(events, seat);
            var entry = With(entries, row);
            var flip = entry.Step(SpotBeats.Row);
            var step = entry.Step(row);
            Assert.IsNotNull(flip, "공개(25행)가 먼저");
            Assert.AreEqual(flip.EndMs, step.StartMs, "뒤집힌 뒤에 결과");
            Assert.AreEqual(ms, step.Ms);
            Assert.AreEqual(ms, PlayTiming.RowMs(ResultRows.EventOf<SpotResolved>(step), row), "방장과 같은 길이");
            Assert.IsTrue(FxBuilt.Drawn(row), "화면이 그리는 행");
            Assert.AreSame(step, ResultRows.SpotStep(entry));
        }

        [Test]
        public void M5_규토리더미의규토리는결과단계끝에착지한다()
        {
            var events = ResultScenes.Spot(ctx, "hidden_spot.red_apricorn_stash");
            var told = (SpotResolved)events.Find(e => e is SpotResolved);
            Assert.AreEqual(3, told.Count, "빨강 3개");
            Assert.Less(events.IndexOf(told), events.FindIndex(e => e is ApricornsGained), "결과가 값보다 먼저 온다");
            var entry = With(Plan(events, told.Seat), ResultRows.Stash);
            var step = entry.Step(ResultRows.Stash);
            var key = ShownKey.Resource(told.Seat, ResKind.Apricorn, (int)ApricornColor.Red);
            var cue = entry.Cues.Find(c => !c.IsSound && c.Key.Equals(key));
            Assert.AreEqual(step.EndMs, cue.AtMs, "규토리 수는 연출이 끝날 때 바뀐다(결과가 먼저 보이지 않는다)");
            Assert.AreEqual(3, cue.After - cue.Before);
        }

        [Test]
        public void M5_결과박자_토큰은터지거나떠오를때사라지고꼬리표는그뒤에뜬다()
        {
            var stash = new SpotResolved { Result = SpotResult.Stash, Count = 3 };
            Assert.AreEqual(1f, SpotResultBeats.At(stash, 0, 1500).Swell);
            Assert.Greater(SpotResultBeats.At(stash, 200, 1500).Swell, 1.2f, "부풀었다가");
            Assert.AreEqual(0f, SpotResultBeats.At(stash, SpotResultBeats.PopMs, 1500).Swell, "터지면 토큰이 없다");
            Assert.AreEqual(SpotResultBeats.PopMs, SpotResultBeats.GoneAt(SpotResult.Stash, 1500));
            Assert.AreEqual(0f, SpotResultBeats.At(stash, 100, 1500).Label);
            Assert.AreEqual(1f, SpotResultBeats.At(stash, 600, 1500).Label);
            Assert.AreEqual(3, SpotResultBeats.Flyers(stash));
            Assert.AreEqual(0f, SpotResultBeats.At(stash, 300, 1500, 2).FlyShow, "셋째 규토리는 아직");
            Assert.AreEqual(1f, SpotResultBeats.At(stash, 300, 1500, 0).FlyShow);
            Assert.AreEqual(1f, SpotResultBeats.At(stash, SpotResultBeats.LandAt(stash), 1500, 2).Fly, "마지막 규토리가 말에 닿았다");
            Assert.LessOrEqual(SpotResultBeats.LandAt(stash), 1500);

            var camp = new SpotResolved { Result = SpotResult.Camp };
            Assert.AreEqual(int.MaxValue, SpotResultBeats.GoneAt(SpotResult.Camp, 1000), "야영지는 남는다");
            Assert.Less(SpotResultBeats.At(camp, 150, 1000).Swell, 1f, "접혔다가");
            Assert.Greater(SpotResultBeats.At(camp, 400, 1000).Swell, 1f, "펴진다");
            Assert.AreEqual(1f, SpotResultBeats.At(camp, 700, 1000).Swell);

            var star = new SpotResolved { Result = SpotResult.Star, Count = 3 };
            Assert.AreEqual(0f, SpotResultBeats.At(star, 100, 1800).Fly);
            Assert.AreEqual(1f, SpotResultBeats.At(star, SpotResultBeats.LiftMs + SpotResultBeats.FlyMs, 1800).Fly);
            Assert.AreEqual(0f, SpotResultBeats.At(star, 1700, 1800).Band, "셋째 별은 띠가 빛나지 않는다");
            var fourth = new SpotResolved { Result = SpotResult.Star, Count = 4 };
            Assert.AreEqual(2600, PlayTiming.RowMs(fourth, ResultRows.Star), "4개째는 +800");
            Assert.Greater(SpotResultBeats.At(fourth, 2200, 2600).Band, 0.9f, "띠가 한 번 빛난다");
            Assert.AreEqual(0f, SpotResultBeats.At(fourth, 1500, 2600).Band);

            var alpha = new SpotResolved { Result = SpotResult.Alpha };
            Assert.AreEqual(1f, SpotResultBeats.At(alpha, SpotResultBeats.FlashAtMs, 2400).Glow, "섬광");
            Assert.AreNotEqual(0f, SpotResultBeats.At(alpha, SpotResultBeats.FlashAtMs + 30, 2400).Shake, "땅울림 한 번");
            Assert.AreEqual(0f, SpotResultBeats.At(alpha, SpotResultBeats.FlashAtMs + 400, 2400).Shake);
            Assert.AreEqual(0, SpotResultBeats.At(alpha, 1000, 2400).Count);
            Assert.AreEqual(SpotResultBeats.HpBonus, SpotResultBeats.At(alpha, 1800, 2400).Count, "체력 +4가 굴러 오른다");
            Assert.AreEqual(2400, SpotResultBeats.GoneAt(SpotResult.Alpha, 2400), "전환까지 남는다");
            Assert.AreEqual(1000, PlayTiming.RowMs(new SpotResolved { Result = SpotResult.AlphaWarded }, ResultRows.Alpha), "피한 우두머리는 칩만");

            var relic = new SpotResolved { Result = SpotResult.Relic };
            Assert.Greater(SpotResultBeats.At(relic, 250, 1800).Glow, 0.9f, "빛기둥");
            Assert.AreEqual(1, SpotResultBeats.Flyers(relic));
            Assert.AreEqual(0f, SpotResultBeats.At(relic, 1790, 1800).FlyShow);
        }

        [Test]
        public void M5_신격파는배틀끝항목에서떼어결과창뒤의새항목으로선다()
        {
            var events = ResultScenes.God(ctx);
            int seat = events.Find(e => e is TokenRemoved t && t.TokenKind == TokenKind.God).Seat;
            var entries = Plan(events, seat);
            var before = entries.Find(x => GodBeats.StepOf(x) != null);
            Assert.IsNotNull(before, "신 격파 단계");
            Assert.IsTrue(before.Events.Exists(e => e is BattleEnded), "엔진은 배틀 끝과 한 행동으로 낸다");
            int length = before.LengthMs, at = entries.IndexOf(before);
            Assert.AreEqual(1, GodBeats.SplitOff(entries));
            var tail = entries[at + 1];
            Assert.IsNull(GodBeats.StepOf(before), "배틀 끝 항목에는 없다");
            Assert.IsNotNull(GodBeats.StepOf(tail));
            Assert.AreEqual(0, GodBeats.StepOf(tail).StartMs);
            Assert.AreEqual(before.Seq, tail.Seq, "같은 행동");
            Assert.AreEqual(length, before.LengthMs + tail.LengthMs, "길이의 합은 그대로(P5)");
            Assert.AreEqual(PlayTiming.RowMs(null, ResultRows.GodGone), tail.LengthMs);
            int t = 0;
            foreach (var s in before.Steps)
            {
                Assert.AreEqual(t, s.StartMs, "남은 단계는 틈 없이 이어진다");
                t += s.Ms;
            }
            GodBeats.At(GodBeats.BannerAtMs + 150, 1800, out float burst, out float banner, out float scale);
            Assert.AreEqual(1f, banner, "「한 번 더!」");
            Assert.Greater(scale, 1f);
            Assert.Less(burst, 1f);
        }

        [Test]
        public void M5_신둘째폼과폼바꾸기는배틀행으로서고기록줄은새폼의이름으로적는다()
        {
            var god = ResultScenes.God(ctx);
            var aura = (FormChanged)god.Find(e => e is FormChanged f && f.Temporary && f.Source.Same(SourceRef.Rule(RuleSource.CelestialAura)));
            Assert.AreEqual(ResultRows.GodPhase, FxRows.Of(aura));
            Assert.AreEqual(ResultRows.GodPhase, PlayTiming.RowOf(aura));
            Assert.IsTrue(BattleRows.Built(ResultRows.GodPhase));
            var back = (FormChanged)god.FindLast(e => e is FormChanged f && !f.Temporary);
            if (back != null) Assert.AreEqual(FxRows.None, FxRows.Of(back), "배틀 끝의 기본 폼 복귀는 단계가 없다");

            var events = ResultScenes.Form(ctx);
            var changed = (FormChanged)events.Find(e => e is FormChanged f && f.Temporary);
            Assert.AreEqual(ResultRows.Form, FxRows.Of(changed));
            Assert.AreEqual(1000, PlayTiming.RowMs(changed, ResultRows.Form));
            // 박자: 가운데에서는 두 모습이 번갈아, 끝에는 새 폼
            Assert.IsFalse(FormBeats.At(100, 1000, false).After);
            Assert.AreNotEqual(FormBeats.At(200 + 10, 1000, false).After, FormBeats.At(200 + FormBeats.FlickerEveryMs + 10, 1000, false).After, "번갈아");
            Assert.IsTrue(FormBeats.At(800, 1000, false).After);
            Assert.AreEqual(1f, FormBeats.At(500, 1000, false).White);
            Assert.AreEqual(0f, FormBeats.At(0, 3200, true).Sink, "첫 폼은 선 채로 시작한다(P29)");
            Assert.That(FormBeats.At(200, 3200, true).Sink, Is.GreaterThan(0f).And.LessThan(FormBeats.SlumpSink), "쓰러지듯 미끄러지는 중");
            Assert.AreEqual(FormBeats.SlumpSink, FormBeats.At(3200 * 20 / 100, 3200, true).Sink, "멈칫 — 다 가라앉지 않고 멈춘다");
            Assert.AreEqual(0f, FormBeats.At(3200 * 20 / 100, 3200, true).White, "멈칫 동안은 흰 빛이 없다");
            Assert.AreEqual(0f, FormBeats.At(300, 3200, true).Pillar);
            var rising = FormBeats.At(3200 * 55 / 100, 3200, true);
            Assert.IsFalse(rising.After);
            Assert.Less(rising.Sink, 1f, "빛기둥 속에서 흰 모습이 솟는다");
            Assert.AreEqual(1f, rising.White);
            var pose = FormBeats.At(3200 * 70 / 100, 3200, true);
            Assert.IsTrue(pose.After, "빛 속에서 둘째 폼");
            Assert.AreEqual(0f, pose.Sink);
            Assert.Greater(pose.Pillar, 0f);
            Assert.AreEqual(1f, FormBeats.At(3100, 3200, true).Fill, "체력이 가득");

            // 기록 줄: 폼이 바뀐 뒤의 줄은 새 폼 이름
            var feed = new BattleFeed(ctx, 0);
            var s = Battles.Game(ctx, 2);
            var all = new List<GameEvent>();
            all.Add(new BattleStarted { BattleId = 1, Kind = BattleKind.Trainer, Sides = new BattleSideInfo[0] });
            all.Add(new SentIn { Side = 0, PokemonId = changed.PokemonId, Species = changed.Before, Hp = 5, MaxHp = 5 });
            all.Add(changed);
            all.Add(new Fainted { PokemonId = changed.PokemonId, Side = 0 });
            feed.Feed(s, all);
            var form = feed.Lines.Find(l => l.Key == BattleFeed.K + "form");
            Assert.IsNotNull(form, "폼 바꾸기 줄");
            Assert.AreEqual("pokemon." + ctx.Cards.Pokemon[changed.Before].Key, form.Args[0].Keys[0]);
            Assert.AreEqual(BattleFeed.FormArg + "pokemon." + ctx.Cards.Pokemon[changed.After].Key, form.Args[1].Keys[0], "새 폼은 폼 꼬리만 적게 표지를 단다");
            var faint = feed.Lines.Find(l => l.Key == BattleFeed.K + "faint");
            Assert.AreEqual("pokemon." + ctx.Cards.Pokemon[changed.After].Key, faint.Args[0].Keys[0], "바뀐 뒤의 줄은 새 폼의 이름");
        }

        [Test]
        public void M5_잠금이유다섯은기록줄과화면글키가있다()
        {
            var table = TestData.LoadStrings("ko");
            foreach (SwitchLockReason reason in System.Enum.GetValues(typeof(SwitchLockReason)))
            {
                string tail = BattleFeed.LockName(reason);
                Assert.IsTrue(table.TryGet(BattleFeed.K + "lock." + tail, out _), reason + " 기록 줄");
                Assert.IsTrue(table.TryGet(RetributionBeats.ReasonKey(reason), out string tip), reason + " 화면 글");
                StringAssert.EndsWith("니다.", tip, "안내는 합쇼체");
            }
            var events = ResultScenes.Locked(ctx);
            var locked = (SwitchLocked)events.Find(e => e is SwitchLocked);
            Assert.IsNotNull(locked, "갤럭시단은 쓰러뜨린 직후 교체하지 못한다");
            Assert.AreEqual(ResultRows.Lock, FxRows.Of(locked));
            Assert.IsTrue(BattleRows.Built(ResultRows.Lock));
            Assert.Greater(RetributionBeats.Lock(0, 400), 1f, "톡");
            Assert.AreEqual(1f, RetributionBeats.Lock(399, 400));
        }

        [Test]
        public void M5_승리보상은결과창의줄로서고_응징과알파의박자()
        {
            var events = ResultScenes.Spoils(ctx, true, out _);
            var taken = (SpoilsTaken)events.Find(e => e is SpoilsTaken);
            Assert.AreEqual(ResultRows.Spoils, FxRows.Of(taken));
            Assert.AreEqual(Scene.Item(ctx, "item.secret_key"), SpoilsBeats.Relic(taken, out int fate));
            Assert.AreEqual(0, fate);
            var cards = (SpoilsTaken)ResultScenes.Spoils(ctx, false, out _).Find(e => e is SpoilsTaken);
            Assert.AreEqual(-1, SpoilsBeats.Relic(cards, out fate));
            Assert.AreEqual(2, fate, "운명 2장");
            Assert.IsTrue(BattleRows.Built(ResultRows.Spoils));

            RetributionBeats.Stored(100, 600, out float fly, out float show, out float pop);
            Assert.Greater(fly, 0f);
            Assert.AreEqual(1f, show);
            Assert.AreEqual(1f, pop, "구슬이 닿기 전에는 배지가 제 크기");
            RetributionBeats.Stored(400, 600, out _, out show, out pop);
            Assert.AreEqual(0f, show, "빨려 든 뒤");
            Assert.Greater(pop, 1f, "배지가 톡");
            RetributionBeats.Moved(1000, 1200, out _, out _, out float burst);
            Assert.Greater(burst, 0f, "터진다");

            Assert.IsTrue(AuraBeats.Strike(0, (int)PokemonType.Fire).On);
            Assert.AreEqual(AuraKind.AlphaStrike, AuraBeats.Strike(300, (int)PokemonType.Fire).Kind);
            Assert.IsFalse(AuraBeats.Strike(AuraBeats.StrikeMs, (int)PokemonType.Fire).On);
            Assert.AreEqual(3, AuraBeats.Boost(0, 1200).Px);
            Assert.AreEqual(AuraBeats.StayPx, AuraBeats.Alpha(5000).Px, "우두머리의 오라는 남는다(결정 80 L15 나 — 2px)");
            Assert.That(AuraBeats.Alpha(5000).Tint, Is.GreaterThan(0f), "남는 오라는 도트에 옅은 색조도 입힌다");
            Assert.AreEqual(AuraKind.God, AuraBeats.God().Kind);
            Assert.AreEqual(1200, PlayTiming.RowMs(new FatigueChanged { Cause = FatigueCause.BeastBoost }, 641));
            Assert.AreEqual(300, PlayTiming.RowMs(new FatigueChanged { Cause = FatigueCause.Effect }, 641));
            Assert.AreEqual(0, SwarmBeats.LineAt(0, 3));
            Assert.AreEqual(3, SwarmBeats.LineAt(SwarmBeats.LineFromMs + 2 * SwarmBeats.LineEveryMs, 3), "약한 것부터 하나씩 셋");
            LegendSiteBeats.At(LegendSiteBeats.QuakeAtMs + 10, out float go, out _, out float quake);
            Assert.AreEqual(1f, go);
            Assert.AreNotEqual(0f, quake, "땅울림");
        }

        [Test]
        public void M9_다듬기6_피규어가솟고_빛으로흩어지고_신의첫폼은기절단계에서서있다()
        {
            // P25 전설 장소(23행): 땅울림 전에는 선 채, 솟았다가 조우 전환 앞 구간의 끝에 내려앉고 클립은 이어진다
            Assert.IsFalse(LegendSiteBeats.Figure(LegendSiteBeats.RiseAtMs - 1, out float hop, out float stretch, out int clip));
            Assert.AreEqual(0f, hop);
            Assert.IsTrue(LegendSiteBeats.Figure(LegendSiteBeats.RiseAtMs + LegendSiteBeats.RiseMs / 2, out hop, out stretch, out clip));
            Assert.AreEqual(1f, hop, 0.01f, "가운데에서 가장 높다");
            Assert.Greater(stretch, 1f);
            Assert.AreEqual(LegendSiteBeats.RiseMs / 2, clip);
            Assert.IsTrue(LegendSiteBeats.Figure(LegendSiteBeats.Ms + 300, out hop, out stretch, out clip), "줄무늬가 덮을 때까지 클립이 이어진다");
            Assert.AreEqual(0f, hop, 0.001f, "내려앉았다");
            Assert.AreEqual(1f, stretch, 0.001f);
            Assert.AreEqual(LegendSiteBeats.Ms, LegendSiteBeats.RiseAtMs + LegendSiteBeats.RiseMs, "솟았다 내려앉는 것은 조우 전환 앞 구간 안(방장 길이 그대로)");

            // P28 · P37 빛으로 흩어짐: 시작 전에는 선 채 → 줄어들며 떠오름 → 사라짐, 알갱이는 조금 더 남는다
            Assert.IsTrue(ScatterBeats.At(-1, out float size, out float lift));
            Assert.AreEqual(1f, size);
            Assert.IsTrue(ScatterBeats.At(ScatterBeats.FlashMs, out size, out lift));
            Assert.AreEqual(1f, size, "잠깐 선 채 빛난다");
            Assert.IsTrue(ScatterBeats.At(ScatterBeats.Ms - 100, out size, out lift));
            Assert.That(size, Is.GreaterThan(0f).And.LessThan(0.5f));
            Assert.Greater(lift, 0.5f);
            Assert.IsFalse(ScatterBeats.At(ScatterBeats.Ms, out size, out lift));
            Assert.AreEqual(0f, size);
            Assert.Less(ScatterBeats.Ms, ScatterBeats.MoteMs);
            int seen = 0;
            for (int i = 0; i < ScatterBeats.Motes; i++)
            {
                Assert.AreEqual(0f, ScatterBeats.Mote(-1, i, out _, out _));
                Assert.AreEqual(0f, ScatterBeats.Mote(ScatterBeats.MoteMs, i, out _, out _));
                if (ScatterBeats.Mote(ScatterBeats.Ms, i, out float x, out float y) > 0f) seen++;
                Assert.That(x, Is.InRange(-1f, 1f));
                Assert.Greater(y, 0f, "위로 떠오른다");
            }
            Assert.AreEqual(ScatterBeats.Motes, seen, "피규어가 사라진 때에도 알갱이는 남아 있다");
            Assert.LessOrEqual(ScatterBeats.MoteMs, PlayTiming.RowMs(new TokenRemoved { TokenKind = TokenKind.God }, ResultRows.GodGone), "신 격파 단계(1800) 안에서 끝난다");
            Assert.Less(LegendBeats.ScatterFromMs + ScatterBeats.Ms, LegendBeats.LandAt, "달빛공터: 있던 전설은 새 전설이 내려앉기 전에 흩어진다");

            Assert.IsNull(GodBeats.Pending(null, out int pending));
            Assert.AreEqual(-1, pending);

            // P29: 둘째 폼 단계가 뒤에 남은 항목이면 그 앞(기절 단계)에서는 가라앉히지 않는다
            var battle = new FxEntry { Seq = 2 };
            var phase = new FxStep { Row = ResultRows.GodPhase, StartMs = 900, Ms = 3200 };
            phase.Events.Add(new FormChanged { PokemonId = 7, Temporary = true });
            battle.Steps.Add(phase);
            Assert.IsTrue(FormBeats.SlumpAhead(battle, 100, 7));
            Assert.IsFalse(FormBeats.SlumpAhead(battle, 100, 8), "다른 포켓몬");
            Assert.IsFalse(FormBeats.SlumpAhead(battle, 900, 7), "단계가 시작되면 780이 그린다");
            Assert.IsFalse(FormBeats.SlumpAhead(null, 0, 7));
        }
    }
}
