using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan §4 「보드 위에 늘 보이는 것」(04 S14·S21, §8-8 4·5행)의 순수 부분: 보드 기록 줄(<see cref="BoardLog"/>) · 막 카드 시간표(<see cref="ActBeats"/>) ·
    /// 조무래기 놓기 시간표(<see cref="TokenBeats"/>) · 숫자 뒤 조사(<see cref="KoJosa"/>). 찾아 둔 판(<see cref="SoloTrail"/>)을 좌석 손님으로 돌려 실제 이벤트로 본다.
    /// </summary>
    public class BoardLogTests
    {
        Ctx ctx;
        StringTable ko;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            ctx = TestData.LoadCtx();
            ko = TestData.LoadStrings("ko");
        }

        sealed class Run
        {
            public readonly BoardLog Log;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            public readonly List<List<FxEntry>> Updates = new List<List<FxEntry>>();
            public GameState View;

            public Run(Ctx ctx) { Log = new BoardLog(ctx); }

            public bool Saw<T>() where T : GameEvent
            {
                foreach (var e in Events) if (e is T) return true;
                return false;
            }

            public FxEntry Entry<T>(int nth = 0) where T : GameEvent
            {
                foreach (var update in Updates)
                    foreach (var entry in update)
                        foreach (var e in entry.Events)
                            if (e is T && nth-- == 0) return entry;
                return null;
            }
        }

        // 화면(GameFlow.Built)과 같은 규칙에서 막 카드·조무래기 놓기만 본다
        static bool Built(FxStep s) => s.Row == ActBeats.Row || (s.Row == TokenBeats.Row && TokenBeats.Grunts(s) != null);

        Run Play(SoloTrail trail, Func<Run, bool> until)
        {
            var run = new Run(ctx);
            var pilot = trail.Pilot();
            var session = SoloGame.Start(ctx, new SoloSettings(), trail.Seed, SoloTrail.AiSeed, new PrincipleAgent(), 0, prepare: trail.Prepared ? trail.Prepare : (Action<GameState>)null);
            var client = new SeatClient(session.Link, ctx, SoloGame.Seat);
            var planner = new FxPlanner(SoloGame.Seat, SoloGame.SeatCount);
            client.Updated += u =>
            {
                pilot.Saw(u.Events);
                run.View = u.View;
                run.Events.AddRange(u.Events);
                run.Log.Feed(u.Events);
                var entries = planner.Plan(u.Events);
                if (u.View.Phase == Phase.Setup) return;
                FxBuilt.KeepSteps(entries, Built);
                run.Updates.Add(entries);
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

        List<string> Texts(Run run)
        {
            var list = new List<string>();
            foreach (var line in run.Log.Lines) list.Add(line.Text(k => ko[k], seat => "플레이어 " + (seat + 1)));
            return list;
        }

        [Test]
        public void M4_기록은셋업부터야생배틀까지받은이벤트를줄로남긴다()
        {
            var run = Play(SoloTrail.Defeat, r => r.Saw<BattleEnded>());
            var texts = Texts(run);
            string all = string.Join("\n", texts);
            // 머리: 게임 준비 → 1라운드
            Assert.AreEqual(ko["screen.log.setup"], texts[0]);
            Assert.AreEqual(BoardLogKind.Round, run.Log.Lines[0].Kind);
            CollectionAssert.Contains(texts, "1라운드");
            // 셋업: 고향 마을 · 스타팅 포켓몬 · 서막 · 조무래기 · 1막 · 빨강 규토리 3개
            string village = ko[ctx.Fixtures.Fixtures[run.View.Seats[0].HomeVillage].Key];
            CollectionAssert.Contains(texts, "플레이어 1은 " + village + "에서 여행을 시작했다");
            StringAssert.Contains("서막 「침입」이 시작되었다", all);
            StringAssert.Contains("갤럭시단 조무래기가 고향 마을을 점거했다!", all);
            StringAssert.Contains("1막 「여정」이 시작되었다", all);
            CollectionAssert.Contains(texts, "플레이어 1은 빨강 규토리 3개를 받았다");
            // 보드: 이동 · 찾아보기 · 조우
            StringAssert.Contains("칸 이동했다", all);
            CollectionAssert.Contains(texts, "플레이어 1은 주변을 찾아보았다");
            StringAssert.Contains("야생 ", all);
            StringAssert.Contains("이 나타났다!", all.Replace("가 나타났다!", "이 나타났다!"));
            // 순서: 서막 → 조무래기 → 1막 → 1라운드 → 이동
            int prologue = texts.FindIndex(t => t.StartsWith("서막", StringComparison.Ordinal)), grunts = texts.FindIndex(t => t.StartsWith("갤럭시단 조무래기가", StringComparison.Ordinal)),
                act1 = texts.FindIndex(t => t.StartsWith("1막", StringComparison.Ordinal)), round1 = texts.IndexOf("1라운드"), moved = texts.FindIndex(t => t.EndsWith("칸 이동했다", StringComparison.Ordinal));
            Assert.That(prologue, Is.GreaterThan(0));
            Assert.That(grunts, Is.GreaterThan(prologue));
            Assert.That(act1, Is.GreaterThan(grunts));
            Assert.That(round1, Is.GreaterThan(act1));
            Assert.That(moved, Is.GreaterThan(round1));
            // 빈칸이 남은 줄 · 조사를 고르지 못한 줄이 없다
            foreach (string t in texts)
            {
                StringAssert.DoesNotContain("{", t);
                StringAssert.DoesNotContain("(는)", t);
                StringAssert.DoesNotContain("(가)", t);
                StringAssert.DoesNotContain("(를)", t);
            }
            // 배틀 안의 줄(기술 · 주사위)은 보드 기록에 없다
            foreach (var line in run.Log.Lines) StringAssert.DoesNotStartWith(BattleFeed.K, line.Line.Key);
        }

        List<string> Texts(BoardLog log)
        {
            var list = new List<string>();
            foreach (var line in log.Lines) list.Add(line.Text(k => ko[k], seat => "플레이어 " + (seat + 1)));
            return list;
        }

        [Test]
        public void M6_UB이벤트의기록줄은짧고UB이름은꼬리를뺀다_이동과균열은한줄로모은다()
        {
            var log = new BoardLog(ctx);
            int nihilego = ctx.Cards.PokemonIds.IndexOf("793:0"), card = ctx.Decks.CardIds.IndexOf("distortion_breach.nimble_frame");
            int starter = ctx.Cards.PokemonIds.IndexOf(ctx.Cards.Pokemon[0].Key);
            log.Feed(new GameEvent[]
            {
                new RoundStarted { Round = 3, Seq = 5 },
                new StarterPicked { Seat = 0, PokemonId = 7, Species = starter, Seq = 5 },
                new UbTurnStarted { Seat = SeatId.System, Round = 3, Seq = 5 },
                new UbMoved { Seat = SeatId.System, UbId = nihilego, From = 1, To = 2, Seq = 5 },
                new UbMoved { Seat = SeatId.System, UbId = nihilego, From = 2, To = 3, Seq = 5 },
                new TokenMoved { Seat = SeatId.System, TokenKind = TokenKind.HiddenSpot, From = 3, To = 40, Seq = 5 },
                new CellDestroyed { Seat = SeatId.System, Cell = 3, TokenKind = TokenKind.ApricornTree, ByUbId = nihilego, Seq = 5 },
                new BreachChanged { Before = 0, After = 3, Seq = 5 },
                new CellDestroyed { Seat = SeatId.System, Cell = 9, TokenKind = TokenKind.Shrine, ByUbId = -1, Seq = 5 },
                new BreachChanged { Before = 3, After = 6, Seq = 5 },
                new BreachCardsDealt { Seq = 5, PerSeat = new[] { new BreachDeal { Seat = 0, CardKeys = new[] { card } } } },
                new BreachCardAttached { Seat = 0, PokemonId = 7, Slot = 1, CardKey = card, Seq = 6 },
                new BlessingUsed { Seat = 0, Target = UbBlessing.SkipTurn, Left = 2, Seq = 7 },
                new UbTurnSkipped { Seat = SeatId.System, Round = 4, Seq = 8 },
                new BlessingGained { Seat = 0, Total = 3, Seq = 9 },
                new NecrozmaPhase { Seat = 0, Phase = 1, Next = 2, Seq = 10 },
                new CoinFlip { Seat = SeatId.System, Result = UbNecrozma.Moon, Seq = 11 },
                new OnslaughtDrawn { Seat = SeatId.UltraBeast, CardKey = UbOnslaught.CardOf(ctx, 5), Effect = 5, Seq = 11 },
                new Eliminated { Seat = 0, Seq = 12 },
                new Revived { Seat = 0, Cell = 4, Seq = 12 },
            });
            var texts = new List<string>();
            foreach (var line in log.Lines)
                if (line.Kind == BoardLogKind.Line && line.Line.Key.StartsWith(BoardLog.K + "ub.", StringComparison.Ordinal)) texts.Add(line.Text(k => ko[k], seat => "플레이어 " + (seat + 1)));
            string mon = ko["pokemon." + ctx.Cards.Pokemon[starter].Key];
            CollectionAssert.AreEqual(new[]
            {
                "울트라 비스트가 움직인다!",
                "텅비드가 2칸 이동했다",
                "밟힌 토큰이 깨어진 세계로 날아갔다",
                "텅비드가 규토리 나무를 부쉈다",
                "균열 강도 0% → 6%",
                "네크로즈마가 사당을 부쉈다",
                KoJosa.Fix("플레이어 1은 왜곡 균열 카드 「" + ko["distortion_breach.nimble_frame"] + "」을(를) 받았다"),
                KoJosa.Fix(mon + "에게 「" + ko["distortion_breach.nimble_frame"] + "」이(가) 붙었다"),
                "플레이어 1은 아르세우스의 축복으로 다음 울트라 비스트의 턴을 막았다(남은 축복 2)",
                "아르세우스의 축복으로 울트라 비스트가 움직이지 않았다",
                "플레이어 1은 아르세우스의 축복을 얻었다(축복 3)",
                "네크로즈마가 2페이즈에 들어섰다!",
                "썬/문 코인 — " + ko["pokemon.800:3"],
                "울트라 온슬롯 — " + ko["ultra_onslaught.5.text.1"],
                "플레이어 1은 네크로즈마에게 져서 리타이어했다…",
                "플레이어 1은 아르세우스의 축복으로 고향 마을에서 다시 일어섰다!",
            }, texts);
            foreach (var t in texts) StringAssert.DoesNotContain("{", t);
        }

        [Test]
        public void M6_울트라버스트솔로판의UB턴은기록줄로남는다()
        {
            var log = new BoardLog(ctx);
            var s = SoloGame.Start(ctx, new SoloSettings { Campaign = Campaign.UltraBurst }, 20261117, SoloTrail.AiSeed, new PrincipleAgent(), 0);
            var c = new SeatClient(s.Link, ctx, SoloGame.Seat);
            int turns = 0;
            c.Updated += u => { log.Feed(u.Events); foreach (var e in u.Events) if (e is UbTurnStarted) turns++; };
            long now = 0;
            for (int i = 0; i < 20000 && turns < 3; i++)
            {
                var ch = c.FirstChoice();
                if (ch == null) { now += 60000; s.Tick(now); continue; }
                var legal = c.Legal(ch);
                var a = ch.Kind == ChoiceKind.MapAction ? legal.Find(x => x.Kind == AnswerKind.EndTurn) ?? legal[0] : legal[0];
                Assert.IsTrue(c.Answer(ch, a, now).Accepted);
            }
            Assert.AreEqual(3, turns);
            var keys = new HashSet<string>();
            foreach (var line in log.Lines)
            {
                keys.Add(line.Line.Key);
                string text = line.Text(k => ko[k], seat => "플레이어 " + (seat + 1));
                StringAssert.DoesNotContain("{", text, line.Line.Key);
                StringAssert.DoesNotContain("(울트라 비스트)", text, "UB 이름은 꼬리를 뺀다");
            }
            foreach (string k in new[] { "ub.turn", "ub.move", "ub.destroy", "ub.breach" }) Assert.IsTrue(keys.Contains(BoardLog.K + k), k);
            Assert.IsFalse(keys.Contains(BoardLog.K + "act"), "울트라 버스트에는 막 줄이 없다");
        }

        static BattleStarted Battle(BattleKind kind, int seat0, int seat1) => new BattleStarted
        {
            Kind = kind,
            Sides = new[] { new BattleSideInfo { Owner = 0, Seat = seat0 }, new BattleSideInfo { Owner = 1, Seat = seat1 } },
        };

        [Test]
        public void M9_도로놓기는보기차이로한줄_없거나빈갱신은줄이없다()
        {
            // worker B의 도로 놓기는 이벤트가 없다 — 화면이 보기의 도로 칸 차이로 센 수를 그 좌석 줄 하나로(GameFlow.RoadDrift)
            var log = new BoardLog(ctx);
            var moved = new GameEvent[] { new Moved { Seat = 1, Path = new[] { 1, 2, 3 }, Seq = 4 } };
            log.RoadDrift(1, 2, moved);
            log.RoadDrift(1, 0, moved);
            log.RoadDrift(1, 3, new GameEvent[0]);
            CollectionAssert.AreEqual(new[] { ko["screen.log.setup"], "플레이어 2는 도로 2칸을 놓았다" }, Texts(log));
            Assert.AreEqual(4, log.Lines[1].Seq, "그 갱신의 마지막 이벤트에 붙는다");
        }

        [Test]
        public void M4_배틀끝줄은이긴좌석과진좌석을가른다()
        {
            // 트레이너 배틀: 0번 쪽(좌석 1)이 이기면 그 좌석이 「이겼다」, 상대 좌석이 「졌다」 — 뒤집히지 않는다
            var log = new BoardLog(ctx);
            log.Feed(new GameEvent[] { Battle(BattleKind.Trainer, 0, 1), new BattleEnded { Winner = 0, Result = BattleResult.Win } });
            CollectionAssert.AreEqual(new[] { ko["screen.log.setup"], "플레이어 1은 승부에서 이겼다!", "플레이어 2는 승부에서 졌다…" }, Texts(log));
            log = new BoardLog(ctx);
            log.Feed(new GameEvent[] { Battle(BattleKind.Trainer, 0, 1), new BattleEnded { Winner = 1, Result = BattleResult.Loss } });
            CollectionAssert.AreEqual(new[] { ko["screen.log.setup"], "플레이어 1은 승부에서 졌다…", "플레이어 2는 승부에서 이겼다!" }, Texts(log));
            // 조무래기 배틀(상대 쪽에 좌석이 없다): 도전 줄 + 내 좌석의 한 줄만
            log = new BoardLog(ctx);
            log.Feed(new GameEvent[] { Battle(BattleKind.Galactic, 0, -1), new BattleEnded { Winner = 0, Result = BattleResult.Win } });
            CollectionAssert.AreEqual(new[] { ko["screen.log.setup"], "플레이어 1은 갤럭시단 조무래기에게 도전했다!", "플레이어 1은 승부에서 이겼다!" }, Texts(log));
            log = new BoardLog(ctx);
            log.Feed(new GameEvent[] { Battle(BattleKind.Galactic, 0, -1), new BattleEnded { Winner = 1, Result = BattleResult.Loss } });
            Assert.AreEqual("플레이어 1은 승부에서 졌다…", Texts(log)[2]);
            // 승자 없음 = 무승부 한 줄, 포획·도망은 끝 줄이 없다(앞 줄이 끝을 알린다)
            log = new BoardLog(ctx);
            log.Feed(new GameEvent[] { Battle(BattleKind.Trainer, 0, 1), new BattleEnded { Winner = -1, Result = BattleResult.Draw } });
            CollectionAssert.AreEqual(new[] { ko["screen.log.setup"], "승부가 나지 않았다…" }, Texts(log));
            log = new BoardLog(ctx);
            log.Feed(new GameEvent[] { Battle(BattleKind.Wild, 0, -1), new BattleEnded { Winner = 0, Result = BattleResult.Captured } });
            Assert.AreEqual(0, log.Lines.Count);
        }

        [Test]
        public void M4_이동줄은걸은칸수를적고_줄어든변화는적지않는다()
        {
            var log = new BoardLog(ctx);
            log.Feed(new GameEvent[]
            {
                new Moved { Seat = 0, Path = new[] { 10, 11, 12, 13 } },   // 칸 넷 = 세 걸음
                new Moved { Seat = 0, Path = new[] { 13 } },               // 제자리는 줄이 없다
                new Moved { Seat = 1, Path = new[] { 5, 6 } },
            });
            CollectionAssert.AreEqual(new[] { ko["screen.log.setup"], "플레이어 1은 3칸 이동했다", "플레이어 2는 1칸 이동했다" }, Texts(log));

            // 배틀 끝·도망·규토리 받기의 변화: 늘어난 것만 적는다(늘어난 만큼). 줄어든 것 · 그대로인 것 · 좌석 없는 것은 줄이 없다
            log = new BoardLog(ctx);
            var changes = new[]
            {
                new ResChange { Seat = 0, Res = ResKind.Jp, Key = -1, Before = 2, After = 5 },
                new ResChange { Seat = 0, Res = ResKind.Apricorn, Key = 0, Before = 4, After = 1 },
                new ResChange { Seat = 0, Res = ResKind.Jp, Key = -1, Before = 5, After = 5 },
                new ResChange { Seat = 0, Res = ResKind.Apricorn, Key = 1, Before = 0, After = 2 },
                new ResChange { Seat = -1, Res = ResKind.Jp, Key = -1, Before = 0, After = 9 },
            };
            log.Feed(new GameEvent[] { Battle(BattleKind.Wild, 0, -1), new BattleEnded { Winner = 0, Result = BattleResult.Win, Changes = changes } });
            CollectionAssert.AreEqual(new[] { ko["screen.log.setup"], "플레이어 1은 승부에서 이겼다!", "플레이어 1은 여행 점수 3점을 얻었다", "플레이어 1은 파랑 규토리 2개를 받았다" }, Texts(log));
            log = new BoardLog(ctx);
            log.Feed(new GameEvent[] { new ApricornsGained { Seat = 0, Changes = new[] { new ResChange { Seat = 0, Res = ResKind.Apricorn, Key = 0, Before = 3, After = 0 } } } });
            Assert.AreEqual(0, log.Lines.Count, "줄어든 변화뿐이면 아무 줄도 없다");
        }

        [Test]
        public void M4_기록은400줄을넘으면오래된줄부터버린다()
        {
            var log = new BoardLog(ctx);
            var batch = new List<GameEvent>();
            for (int i = 0; i < BoardLog.MaxLines + 50; i++) batch.Add(new Moved { Seat = 0, Path = new int[i % 5 + 2] });
            log.Feed(batch);
            Assert.AreEqual(BoardLog.MaxLines, log.Lines.Count);
            // 남은 것은 가장 새 줄들이다: 마지막 줄 = 마지막 이벤트, 첫 줄 = 51번째 줄(머리 한 줄 + 이동 450줄에서 앞 51줄이 빠졌다)
            Assert.AreSame(batch[batch.Count - 1], log.Lines[log.Lines.Count - 1].Line.Event);
            Assert.AreSame(batch[50], log.Lines[0].Line.Event);
            // 갱신을 나눠 받아도 같다
            log.Feed(new GameEvent[] { new Moved { Seat = 1, Path = new int[2] } });
            Assert.AreEqual(BoardLog.MaxLines, log.Lines.Count);
            Assert.AreSame(batch[51], log.Lines[0].Line.Event);
        }

        [Test]
        public void M4_이어받은판의기록은그라운드머리와이어서시작한줄로시작한다()
        {
            // 04 §9-12 [가정]: 지나간 줄은 저장에 없다 — 이어 받은 판의 기록은 「n라운드」 머리 + 「저장한 곳에서 이어서 시작했다」 한 줄
            var log = new BoardLog(ctx);
            log.Resumed(3, 2);
            Assert.AreEqual(2, log.Lines.Count);
            Assert.AreEqual(BoardLogKind.Round, log.Lines[0].Kind);
            Assert.AreEqual("3라운드", log.Lines[0].Text(k => ko[k], null));
            Assert.AreEqual(BoardLogKind.Line, log.Lines[1].Kind);
            Assert.AreEqual(-1, log.Lines[1].Seat, "좌석 없는 줄");
            Assert.AreEqual("저장한 곳에서 이어서 시작했다", log.Lines[1].Text(k => ko[k], seat => "플레이어 " + (seat + 1)));
            // 대기열이 돌고 있어도 보인다(행동의 줄이 아니다)
            var run = Play(SoloTrail.Defeat, r => r.Saw<TurnStarted>());
            var seq = new FxSequencer();
            foreach (var update in run.Updates) seq.Enqueue(update);
            seq.Advance(1);
            Assert.IsTrue(log.Shows(log.Lines[1], seq));
            // 뒤에 오는 막 줄의 번호가 이어진다(공개된 막 둘 = 서막·1막 → 다음은 2막), 줄은 그 라운드에 선다
            log.Feed(new GameEvent[] { new ActRevealed { Seq = 7 } });
            Assert.AreEqual(BoardLog.K + "act", log.Lines[2].Line.Key);
            StringAssert.StartsWith("2막", log.Lines[2].Text(k => ko[k], null));
            Assert.AreEqual(3, log.Lines[2].Round);
            // 셋업 가운데에서 이어 받으면 「게임 준비」 머리
            var setup = new BoardLog(ctx);
            setup.Resumed(0, 0);
            Assert.AreEqual("게임 준비", setup.Lines[0].Text(k => ko[k], null));
        }

        [Test]
        public void M10_무르기_되돌린판의기록은지운미래를빼고무르기줄을더한다()
        {
            // undo.md §3-5 [가정 U13]: 되돌린 자리 뒤(Seq ≥ NextSeq)의 줄은 빠지고 「○○은(는) 무르기를 했다」 한 줄 — 같은 라운드면 머리를 다시 넣지 않는다
            var log = new BoardLog(ctx);
            log.Feed(new GameEvent[]
            {
                new RoundStarted { Round = 2, Seq = 10 }, new Moved { Seat = 0, Path = new int[3], Seq = 11 }, new Moved { Seat = 0, Path = new int[2], Seq = 14 },
                new RoundStarted { Round = 3, Seq = 20 }, new Moved { Seat = 0, Path = new int[2], Seq = 21 },
            });
            log.Rewound(14, 2, 2, 0, false);
            CollectionAssert.AreEqual(new[] { "2라운드", "플레이어 1은 2칸 이동했다", "플레이어 1은 무르기를 했다" }, Texts(log));
            Assert.AreEqual(0, log.Lines[2].Seat, "무른 좌석의 줄");
            Assert.AreEqual(2, log.Lines[2].Round);
            Assert.AreEqual(BoardLog.ResumedSeq, log.Lines[2].Seq, "행동의 줄이 아니다 — 대기열과 무관하게 보인다");
            Assert.IsTrue(log.Shows(log.Lines[2], new FxSequencer()));
            // 방장 되돌리기(지난 라운드로): 그 라운드 머리 + 「○○은(는) n라운드로 되돌렸다」, 앞서 남긴 무르기 줄은 지난 기록으로 남는다
            log.Rewound(11, 1, 1, 0, true);
            CollectionAssert.AreEqual(new[] { "2라운드", "플레이어 1은 무르기를 했다", "1라운드", "플레이어 1은 1라운드로 되돌렸다" }, Texts(log));
            Assert.AreEqual(BoardLogKind.Round, log.Lines[2].Kind);
            // 그 뒤 새 이벤트의 줄은 되돌린 라운드에 선다
            log.Feed(new GameEvent[] { new Moved { Seat = 0, Path = new int[2], Seq = 11 } });
            Assert.AreEqual(1, log.Lines[log.Lines.Count - 1].Round);
        }

        [Test]
        public void M10_무르기_누가했는지모르는되돌림은지운미래만빼고무르기줄은없다()
        {
            // undo.md [가정 U9]: 끊긴 동안의 되돌림(재접속 스냅샷의 Epoch로 알아챔 — by = −1)은 같은 Seq가 다시 쓰이므로 지운 미래의 줄만 뺀다
            var log = new BoardLog(ctx);
            log.Feed(new GameEvent[]
            {
                new RoundStarted { Round = 2, Seq = 10 }, new Moved { Seat = 0, Path = new int[3], Seq = 11 }, new Moved { Seat = 1, Path = new int[2], Seq = 14 },
            });
            log.Rewound(14, 2, 2, -1, false);
            CollectionAssert.AreEqual(new[] { "2라운드", "플레이어 1은 2칸 이동했다" }, Texts(log));
            // 지난 라운드로 되돌렸으면 그 라운드 머리는 선다
            log.Rewound(11, 1, 1, -1, true);
            CollectionAssert.AreEqual(new[] { "2라운드", "1라운드" }, Texts(log));
        }

        [Test]
        public void M4_기록줄은그행동의연출이끝난뒤에보인다()
        {
            var run = Play(SoloTrail.Defeat, r => r.Saw<TurnStarted>());
            var seq = new FxSequencer();
            Assert.IsTrue(run.Log.Shows(run.Log.Lines[run.Log.Lines.Count - 1], null), "대기열이 없으면 다 보인다");
            // 셋업이 끝난 갱신의 항목(서막 → 조무래기 → 1막 → 첫 턴)을 대기열에 넣으면 그 줄들은 재생이 끝날 때까지 숨는다
            foreach (var update in run.Updates) seq.Enqueue(update);
            var shown = new List<BoardLogLine>();
            seq.Advance(1);   // 길이 0인 항목(스타팅 포켓몬 · 트레이너 카드 — 셋업 연출은 아직 없다)은 그 자리에서 지나가고 서막 카드가 돈다
            run.Log.Shown(seq, shown);
            int before = shown.Count;
            Assert.That(before, Is.LessThan(run.Log.Lines.Count), "아직 재생하지 않은 행동의 줄은 숨는다");
            foreach (var line in shown) Assert.AreNotEqual(BoardLog.K + "act", line.Line.Key, "서막 카드가 도는 동안에는 「서막이 시작되었다」가 아직 없다");
            // 서막 카드가 도는 동안에는 「서막이 시작되었다」가 아직 없다 — 끝나면 붙는다
            seq.Advance(3598);
            shown.Clear();
            run.Log.Shown(seq, shown);
            Assert.AreEqual(before, shown.Count, "막 카드가 끝나기 전");
            seq.Advance(1);
            shown.Clear();
            run.Log.Shown(seq, shown);
            Assert.That(shown.Count, Is.GreaterThan(before), "막 카드가 끝나면 그 줄이 보인다");
            Assert.AreEqual(BoardLog.K + "act", shown[shown.Count - 1].Line.Key);
            // 다 재생하면 모두 보인다
            seq.Advance(60000);
            shown.Clear();
            run.Log.Shown(seq, shown);
            Assert.AreEqual(run.Log.Lines.Count, shown.Count);
        }

        [Test]
        public void M4_막카드는게임시작에서막_조무래기_1막순서로서고위띠배지는착지때바뀐다()
        {
            var run = Play(SoloTrail.Defeat, r => r.Saw<TurnStarted>());
            var prologue = run.Entry<ActRevealed>(0);
            var grunts = run.Entry<TokensPlaced>(1);   // 0번째 = 숨겨진 장소 배치(게임 시작 항목 — 화면이 그리지 않는다)
            var act1 = run.Entry<ActRevealed>(1);
            Assert.IsNotNull(ActBeats.StepOf(prologue));
            Assert.IsNotNull(ActBeats.StepOf(act1));
            Assert.AreEqual(3600, ActBeats.StepOf(prologue).Ms, "04 §8-8 4행 — 방장과 같은 길이(PlayTiming 40행)");
            Assert.AreEqual(FxLayer.Block, ActBeats.StepOf(prologue).Layer, "전원 차단 컷신");
            Assert.IsNotNull(TokenBeats.StepOf(grunts), "서막의 조무래기 놓기(5행)");
            Assert.AreEqual(1500 + 300, TokenBeats.StepOf(grunts).Ms, "1500 + 마을당 300(혼자 하기 = 마을 하나)");
            Assert.IsNull(TokenBeats.StepOf(run.Entry<TokensPlaced>(0)), "숨겨진 장소 배치 단계는 덜어 냈다");
            Assert.AreEqual(0, run.Entry<TokensPlaced>(0).LengthMs, "그릴 것이 없는 항목은 길이 0");

            // 위 띠 막 배지: 보기는 이미 1막이지만 서막 카드가 착지하기 전에는 배지가 없고(−1), 착지하면 서막, 1막 카드가 착지하면 1막
            var seq = new FxSequencer();
            Assert.AreEqual(1, ActBeats.ShownAct(1, null));
            foreach (var update in run.Updates) seq.Enqueue(update);
            Assert.AreEqual(-1, ActBeats.ShownAct(1, seq));
            int land = ActBeats.LandAt(3600);
            Assert.AreEqual(3200, land);
            seq.Advance(land - 1);
            Assert.AreEqual(-1, ActBeats.ShownAct(1, seq));
            seq.Advance(1);
            Assert.AreEqual(0, ActBeats.ShownAct(1, seq), "서막 카드 착지");
            seq.Advance(3600 - land + 1800 + land - 1);
            Assert.AreEqual(0, ActBeats.ShownAct(1, seq), "1막 카드가 착지하기 전");
            seq.Advance(1);
            Assert.AreEqual(1, ActBeats.ShownAct(1, seq), "1막 카드 착지");
        }

        [Test]
        public void M4_막카드박자는어두워짐_떨어짐_글자박힘_흔들림_착지()
        {
            const int T = 3600, letters = 2;
            var p = ActBeats.At(-1, T, letters);
            Assert.AreEqual(0f, p.Dim);
            Assert.AreEqual(0f, p.Card);
            // 어두워짐(길이 / 12 = 300): 카드는 아직 없다
            p = ActBeats.At(150, T, letters);
            Assert.AreEqual(0.5f, p.Dim, 1e-4f);
            Assert.AreEqual(0f, p.Card);
            // 떨어짐(300 ~ 750): 위에서 내려오며 가로로 펴진다
            p = ActBeats.At(300, T, letters);
            Assert.AreEqual(1f, p.Dim, 1e-4f);
            Assert.AreEqual(-1f, p.Drop, 1e-4f);
            Assert.AreEqual(0f, p.Flip, 1e-4f);
            p = ActBeats.At(750, T, letters);
            Assert.AreEqual(0f, p.Drop, 1e-4f);
            Assert.AreEqual(1f, p.Flip, 1e-4f);
            Assert.AreEqual(0, p.Letters, "글자는 한 박자 뒤부터");
            // 글자: 810부터 70ms 간격, 2배 → 1배(140ms)
            Assert.AreEqual(810, ActBeats.LettersAt(T));
            p = ActBeats.At(810, T, letters);
            Assert.AreEqual(1, p.Letters);
            Assert.AreEqual(2f, p.Stamp, 1e-4f);
            p = ActBeats.At(810 + 70, T, letters);
            Assert.AreEqual(2, p.Letters);
            Assert.AreEqual(2f, p.Stamp, 1e-4f);
            p = ActBeats.At(810 + 70 + 140, T, letters);
            Assert.AreEqual(1f, p.Stamp, 1e-4f);
            Assert.AreEqual(810 + 70 + 140, ActBeats.StampedAt(T, letters));
            // 흔들림: 마지막 글자가 박힌 뒤 260ms 동안 한 번(감쇠), 그 밖에는 0
            Assert.AreEqual(0f, ActBeats.At(1019, T, letters).Shake);
            Assert.AreNotEqual(0f, ActBeats.At(1020 + 30, T, letters).Shake);
            Assert.AreEqual(0f, ActBeats.At(1020 + 260, T, letters).Shake);
            // 목표 문장 → 보상 줄
            Assert.AreEqual(0f, ActBeats.At(1020 + 130, T, letters).Goal);
            Assert.AreEqual(1f, ActBeats.At(1020 + 130 + 160, T, letters).Goal, 1e-4f);
            Assert.AreEqual(0f, ActBeats.At(1020 + 130 + 220, T, letters).Reward);
            Assert.AreEqual(1f, ActBeats.At(1020 + 130 + 220 + 160, T, letters).Reward, 1e-4f);
            // 머묾 → 착지(3200)부터 걷힌다
            p = ActBeats.At(3199, T, letters);
            Assert.IsFalse(p.Landed);
            Assert.AreEqual(1f, p.Card, 1e-4f);
            p = ActBeats.At(3400, T, letters);
            Assert.IsTrue(p.Landed);
            Assert.AreEqual(0.5f, p.Card, 1e-4f);
            Assert.AreEqual(0.5f, p.Dim, 1e-4f);
            Assert.AreEqual(0f, ActBeats.At(T, T, letters).Card, "단계가 끝나면 없다");
        }

        [Test]
        public void M4_조무래기토큰은마을마다150ms어긋나게떨어진다()
        {
            Assert.AreEqual(-1f, TokenBeats.Fall(299, 0), "예비 300 동안은 안 보인다");
            Assert.AreEqual(0f, TokenBeats.Fall(300, 0), 1e-4f);
            Assert.AreEqual(1f, TokenBeats.Fall(300 + 320, 0), 1e-4f);
            Assert.AreEqual(-1f, TokenBeats.Fall(449, 1), "둘째 마을은 150 뒤");
            Assert.AreEqual(0f, TokenBeats.Fall(450, 1), 1e-4f);
            Assert.That(TokenBeats.Fall(460, 0), Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(1f, TokenBeats.Squash(619, 0), "내려앉기 전에는 제 모양");
            Assert.AreEqual(0.7f, TokenBeats.Squash(620, 0), 1e-4f, "내려앉는 순간 가장 납작");
            Assert.AreEqual(1f, TokenBeats.Squash(620 + 140, 0), 1e-4f);
            // 여섯 마을이 다 내려앉아도 단계 길이(1500 + 300 × 6 = 3300) 안이다
            Assert.That(TokenBeats.LeadMs + TokenBeats.EveryMs * 5 + TokenBeats.FallMs + TokenBeats.SquashMs, Is.LessThan(1500 + 300 * 6));
        }

        [Test]
        public void M4_숫자뒤조사는읽는소리의받침을따른다()
        {
            Assert.AreEqual("플레이어 1은 · 플레이어 2는 · 플레이어 3은 · 플레이어 4는 · 플레이어 5는 · 플레이어 6은",
                KoJosa.Fix("플레이어 1은(는) · 플레이어 2은(는) · 플레이어 3은(는) · 플레이어 4은(는) · 플레이어 5은(는) · 플레이어 6은(는)"));
            Assert.AreEqual("7이 · 8을 · 9가 · 0과", KoJosa.Fix("7이(가) · 8을(를) · 9이(가) · 0과(와)"));
            Assert.AreEqual("1로 · 3으로 · 7로 · 8로 · 2로", KoJosa.Fix("1(으)로 · 3(으)로 · 7(으)로 · 8(으)로 · 2(으)로"), "일·칠·팔은 ㄹ 받침");
            Assert.AreEqual("Rekkon은(는)", KoJosa.Fix("Rekkon은(는)"), "로마자는 그대로");
            Assert.AreEqual("서막 「침입」이 시작되었다 · 「여정」이 · 『심장』을", KoJosa.Fix("서막 「침입」이(가) 시작되었다 · 「여정」이(가) · 『심장』을(를)"), "닫는 낫표는 건너뛴다");
        }
    }
}
