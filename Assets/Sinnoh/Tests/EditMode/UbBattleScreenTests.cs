using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「UB 배틀 화면 묶음」의 엔진 무관 셈(<c>UbBattleStaging.cs</c> · <see cref="BattleFeed"/>): 프리즘 아머 880·881 · 울트라 온슬롯 890 · 네크로즈마 페이즈 900 · 동전 901 · 호수 시련 920·921의
    /// 방장·화면 행과 길이 · 배틀 기록 줄(UB 등장 · 프리즘 · 온슬롯 · 시련) · 페이즈 격파 단계가 배틀 끝 항목에서 떨어져 결과 창 [확인] 뒤에 서는 것 · 박자.
    /// </summary>
    public class UbBattleScreenTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        [Test]
        public void M6_UB배틀행_방장과화면이같은행과길이와그리는곳()
        {
            var src = SourceRef.Rule(RuleSource.LakeGuardian);
            var events = new (GameEvent e, int row, int ms, bool battle)[]
            {
                (new PrismReduced { Seat = SeatId.UltraBeast, Side = 1, PokemonId = 3, Amount = 2 }, UbBattleRows.Prism, 600, true),
                (new PrismMoved { Seat = SeatId.System, Before = new[] { 9, 0 }, After = new[] { 8, 1 } }, UbBattleRows.PrismMove, 1000, true),
                (new OnslaughtDrawn { Seat = SeatId.UltraBeast, Source = src, CardKey = 1, Effect = 5 }, UbBattleRows.Onslaught, 1000, true),
                (new NecrozmaPhase { Seat = 0, Phase = 1, Next = 2 }, UbBattleRows.Phase, 2500, false),
                (new CoinFlip { Seat = SeatId.System, Result = 1 }, UbBattleRows.Coin, 1500, false),
                (new TrialProgress { Seat = 0, Lake = 2, Count = 1, Goal = 2, Done = false }, UbBattleRows.Trial, 600, true),
                (new TrialProgress { Seat = 0, Lake = 2, Count = 2, Goal = 2, Done = true }, UbBattleRows.Trial, 1500, true),
                (new GuardianDown { Seat = 0, Lake = 2, Total = 1 }, UbBattleRows.Guardian, 1500, true),
            };
            foreach (var (e, row, ms, battle) in events)
            {
                Assert.AreEqual(row, PlayTiming.RowOf(e), e.GetType().Name + " 방장 행");
                Assert.AreEqual(row, FxRows.Of(e), e.GetType().Name + " 화면 행");
                Assert.AreEqual(ms, PlayTiming.RowMs(e, row), e.GetType().Name + " 길이");
                Assert.IsTrue(FxBuilt.Drawn(row), row + "행은 화면이 그린다");
                Assert.AreEqual(battle, BattleRows.Built(row), row + "행은 배틀 화면이 그리나");
                Assert.AreEqual(!battle, UbBattleRows.Board(row), row + "행은 보드가 그리나");
                Assert.IsTrue(SoundCues.Covers(row), row + "행 소리 표");
                Assert.IsFalse(FxRows.BlocksAll(row), row + "행은 전원 차단이 아니다");
            }
        }

        BattleFeed Feed(BattleKind kind, GameState view, params GameEvent[] events)
        {
            var feed = new BattleFeed(ctx, 0);
            var all = new List<GameEvent> { new BattleStarted { Seat = 0, BattleId = 1, Kind = kind, Cell = 1, Sides = new[] { new BattleSideInfo { Owner = 0 }, new BattleSideInfo { Owner = SeatId.UltraBeast } } } };
            all.AddRange(events);
            feed.Feed(view, all);
            return feed;
        }

        static List<string> Keys(BattleFeed feed)
        {
            var keys = new List<string>();
            foreach (var l in feed.Lines) keys.Add(l.Key);
            return keys;
        }

        [Test]
        public void M6_UB배틀기록줄은등장프리즘온슬롯을적고페이즈격파는배틀끝뒤에받는다()
        {
            var s = UbScenes.Started(ctx, 1, 3);
            int ub = PokemonRules.Spawn(s, Scene.Def(ctx, "797:0"), SeatId.UltraBeast, -1);
            var feed = Feed(BattleKind.UltraBeast, s,
                new SentIn { Seat = SeatId.UltraBeast, Side = 1, PokemonId = ub, Species = Scene.Def(ctx, "797:0"), Hp = 21 },
                new PrismReduced { Seat = SeatId.UltraBeast, Side = 1, PokemonId = ub, Amount = 2 },
                new OnslaughtDrawn { Seat = SeatId.UltraBeast, Source = SourceRef.Rule(RuleSource.LakeGuardian), CardKey = 3, Effect = 5 },
                new PrismMoved { Seat = SeatId.System, Before = new[] { 9, 0 }, After = new[] { 8, 1 } },
                new BattleEnded { Seat = 0, BattleId = 1, Winner = 0, Result = BattleResult.Win, Changes = new ResChange[0] },
                new NecrozmaPhase { Seat = 0, Phase = 1, Next = 2 });
            var keys = Keys(feed);
            CollectionAssert.Contains(keys, BattleFeed.K + "send.ub", "UB 등장 줄(야생 줄이 아니다)");
            CollectionAssert.DoesNotContain(keys, BattleFeed.K + "send.wild");
            CollectionAssert.Contains(keys, BattleFeed.K + "prism");
            CollectionAssert.Contains(keys, BattleFeed.K + "prism.moved");
            var line = feed.Lines.Find(l => l.Key == BattleFeed.K + "onslaught");
            Assert.IsNotNull(line);
            Assert.AreEqual("screen.battle.onslaught.5", line.Args[0].Keys[0], "카드 번호의 짧은 이름");
            Assert.IsNotNull(feed.PrismMove);
            Assert.AreEqual(1, feed.PrismMove.After[1]);
            Assert.IsNotNull(feed.PhaseDown, "배틀 끝 뒤의 페이즈 격파는 결과 창의 줄");
            Assert.AreEqual(2, feed.PhaseDown.Next);
            Assert.AreEqual("ub", BattleFeed.UbFoe(BattleKind.UltraBeast));
            Assert.AreEqual("necrozma", BattleFeed.UbFoe(BattleKind.Necrozma));
            Assert.AreEqual("trial", BattleFeed.UbFoe(BattleKind.Trial));
            Assert.IsNull(BattleFeed.UbFoe(BattleKind.Wild));
        }

        [Test]
        public void M6_시련배틀은수호신이나오면그시련조건줄을적고넘긴시련이면적지않는다()
        {
            int def = UbTrial.Guardian(ctx, (int)Shard.Willpower);
            foreach (bool overcome in new[] { false, true })
            {
                var s = UbScenes.Started(ctx, 1, 3);
                s.Ub.TrialsOvercome[(int)Shard.Willpower] = overcome;
                int az = PokemonRules.Spawn(s, def, SeatId.Wild, -1);
                var feed = Feed(BattleKind.Trial, s, new SentIn { Seat = SeatId.Wild, Side = 1, PokemonId = az, Species = def, Hp = 20 },
                    new TrialProgress { Seat = 0, Lake = (int)Shard.Willpower, Count = 2, Goal = 2, Done = true });
                var keys = Keys(feed);
                Assert.AreEqual((int)Shard.Willpower, feed.TrialLake);
                CollectionAssert.Contains(keys, BattleFeed.K + "send.trial");
                Assert.AreEqual(!overcome, keys.Contains(BattleFeed.K + "trial.start." + (int)Shard.Willpower), "시련 조건 줄(넘긴 시련이면 없다)");
                CollectionAssert.Contains(keys, BattleFeed.K + "trial.done");
            }
        }

        [Test]
        public void M6_네크로즈마페이즈격파단계는배틀끝항목에서떨어져뒤의항목으로선다()
        {
            var entry = new FxEntry { Seq = 7, Seat = 0 };
            var end = new BattleEnded { Seat = 0, BattleId = 1, Winner = 0, Result = BattleResult.Win, Changes = new ResChange[0] };
            var phase = new NecrozmaPhase { Seat = 0, Phase = 1, Next = 2 };
            var endStep = new FxStep { Row = 700, StartMs = 0, Ms = 3000 };
            endStep.Events.Add(end);
            var phaseStep = new FxStep { Row = UbBattleRows.Phase, StartMs = 3000, Ms = 2500 };
            phaseStep.Events.Add(phase);
            entry.Steps.Add(endStep);
            entry.Steps.Add(phaseStep);
            entry.Events.Add(end);
            entry.Events.Add(phase);
            entry.QueueMs = 5500;
            var entries = new List<FxEntry> { entry };
            Assert.AreEqual(1, UbBattleRows.SplitPhase(entries));
            Assert.AreEqual(2, entries.Count);
            Assert.IsNull(entries[0].Step(UbBattleRows.Phase), "배틀 끝 항목에는 페이즈 단계가 없다");
            var tail = entries[1];
            Assert.AreEqual(7, tail.Seq);
            Assert.AreEqual(0, tail.Steps[0].StartMs);
            Assert.AreEqual(UbBattleRows.Phase, tail.Steps[0].Row);
            Assert.IsTrue(tail.Events.Contains(phase));
            Assert.AreEqual(5500, entries[0].QueueMs + tail.QueueMs, "두 항목 길이의 합은 그대로(P5)");
            // 배틀 끝이 없는 항목은 떼지 않는다
            var lone = new FxEntry { Seq = 8 };
            lone.Steps.Add(new FxStep { Row = UbBattleRows.Phase, Ms = 2500 });
            Assert.AreEqual(0, UbBattleRows.SplitPhase(new List<FxEntry> { lone }));
        }

        [Test]
        public void M6_UB배틀박자_온슬롯은뒤집힌뒤앞면이고동전은결과면으로내려앉는다()
        {
            var rising = OnslaughtBeats.At(100, 1000);
            Assert.IsFalse(rising.Front, "떠오르는 동안은 뒷면");
            Assert.AreNotEqual(0, rising.Shake, "떠오르며 떤다");
            var front = OnslaughtBeats.At(600, 1000);
            Assert.IsTrue(front.Front);
            Assert.Greater(front.Glow, 0f, "효과 빛");
            Assert.Less(OnslaughtBeats.At(990, 1000).Scale, 0.5f, "상자 옆으로 줄어 붙는다");
            for (int result = 0; result < 2; result++)
            {
                var landed = CoinBeats.At(1400, 1500, result);
                Assert.IsTrue(landed.Landed);
                Assert.AreEqual(result, landed.Face, "착지하면 결과 면");
                Assert.Greater(landed.Label, 0.9f);
            }
            Assert.IsFalse(CoinBeats.At(400, 1500, 0).Landed);
            PrismBeats.Move(300, 1000, out _, out float fly, out bool arrived);
            Assert.Greater(fly, 0f);
            Assert.IsFalse(arrived, "조각이 닿기 전에는 숫자가 그대로");
            PrismBeats.Move(900, 1000, out _, out _, out arrived);
            Assert.IsTrue(arrived);
            TrialBeats.Pass(1499, 1500, out float shield, out _, out _, out _);
            Assert.AreEqual(0f, shield, 0.001f, "넘긴 단계 끝에는 면역 막이 없다");
        }
    }
}
