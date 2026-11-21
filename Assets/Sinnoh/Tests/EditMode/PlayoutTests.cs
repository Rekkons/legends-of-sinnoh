using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M3 완료(M3-h) · M5 조건 3(「막 뼈대」): 봇 플레이아웃 1~6인 × 시드 0..PlayoutSeeds−1을 종막(게임 끝)까지(관문은 줄인 판 — dotnet 20시드 120판 · Unity 10시드 60판, 600판 전체는 SINNOH_PLAYOUT_SEEDS=100), 복제 시간(조건 4, 01 §13 #12), 방장 멈춤 = 좌석별 최댓값(조건 5, 02 §5-7).
    /// 시간은 시험이 잰다(Stopwatch) — Rules는 시계를 읽지 않는다. 결과 숫자는 TestContext에 적고 plan §0·§4에 옮긴다.
    /// </summary>
    public class PlayoutTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        public const int Seeds = 100;
        /// <summary>
        /// 종막 봇 판의 인원마다 시드 수. 관문(dotnet fast·새 사본)은 20(120판), Unity EditMode는 10(60판) — 대역을 뺀 판이 길어져(11-15, 판 평균 명령 약 1만) 600판이 관문을 7분 늘렸다.
        /// 전체 600판(완료 조건 3)은 확인 지점 감사에서 환경 변수 SINNOH_PLAYOUT_SEEDS=100으로 dotnet을 한 번 돌린다(규칙 tests).
        /// </summary>
#if UNITY_EDITOR
        public static readonly int PlayoutSeeds = 10;
#else
        public static readonly int PlayoutSeeds = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_PLAYOUT_SEEDS"), out var n) && n > 0 ? n : 20;
#endif
        /// <summary>세션 판(방장 멈춤 시험) 시드 수와 판마다 명령 수. 이 판은 300명령까지만 돌아 1막 해방까지 가지 않는다.</summary>
        public const int SessionSeeds = 10;
        public const int SessionCommands = 300;

        static void Report(string line)
        {
            TestContext.WriteLine(line);
            TestContext.Progress.WriteLine(line);
        }

        [Test, Category("Slow")]
        public void M5_봇플레이아웃종막()
        {
            // plan M5 조건 3: 1~6인 × 시드 100이 예외 없이 게임 끝(영웅 / 배신자 연전)에 닿는다 — 대역 없이(「봇 종막 대역 0」 — 봇이 진화·지니기·기술·회복으로 이긴다). 막마다 닿은 판 수를 적는다.
            // 관문은 줄인 판(PlayoutSeeds — dotnet 20 · Unity 10), 600판 전체는 확인 지점에서 SINNOH_PLAYOUT_SEEDS=100
            int Seeds = PlayoutSeeds;
            var sw = Stopwatch.StartNew();
            var errors = new List<string>();
            int games = 0, maxCommands = 0, maxRounds = 0, total = 0;
            string maxRoundsAt = "";
            long commands = 0, timeouts = 0, captures = 0, balls = 0, crafts = 0, trades = 0, evolutions = 0, fates = 0, shrines = 0, shrineExtras = 0, shrineShared = 0, descents = 0, remotes = 0, seatTrades = 0, maint = 0, silver = 0, gold = 0, seatSum = 0;
            var battles = new long[Enum.GetValues(typeof(BattleKind)).Length];
            var stamps = new long[SeatState.StampCount];
            var reached = new int[HosState.Acts];
            var reasons = new int[Enum.GetValues(typeof(GameEndReason)).Length];
            var lines = new List<string>();
            int cardCount = ctx.Cards.TrainerCards.Count;
            var abilityUses = new long[cardCount * 2];
            var cardSeats = new long[cardCount];
            var cardGold = new long[cardCount];
            long secondHeld = 0, snaps = 0;
            var maintKinds = new long[Enum.GetValues(typeof(MaintKind)).Length];
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
            {
                long seatCommands = 0, seatPause = 0, seatRounds = 0;
                for (ulong seed = 0; seed < (ulong)Seeds; seed++)
                {
                    var g = new Playout.Game();
                    total++;
                    try
                    {
                        Playout.Run(ctx, seats, seed, null, g);
                        games++;
                        reasons[(int)g.Reason]++;
                        commands += g.Commands;
                        seatCommands += g.Commands;
                        timeouts += g.Timeouts;
                        captures += g.Captures;
                        balls += g.BallsCrafted;
                        crafts += g.OtherCrafted;
                        trades += g.Trades;
                        evolutions += g.Evolutions;
                        fates += g.FatePlays;
                        shrines += g.ShrineOffers;
                        shrineExtras += g.ShrineExtras;
                        shrineShared += g.ShrineShared;
                        seatPause += g.PauseMs;
                        seatRounds += g.Rounds;
                        maxCommands = Math.Max(maxCommands, g.Commands);
                        if (g.Rounds > maxRounds) maxRoundsAt = $"{seats}인 시드 {seed}";
                        maxRounds = Math.Max(maxRounds, g.Rounds);
                        for (int k = 0; k < battles.Length; k++) battles[k] += g.Battles[k];
                        for (int k = 0; k < stamps.Length; k++) stamps[k] += g.Stamps[k];
                        descents += g.GodDescents;
                        remotes += g.RemoteChallenges;
                        seatTrades += g.SeatTrades;
                        silver += g.SilverSeats;
                        gold += g.GoldSeats;
                        seatSum += seats;
                        maint += g.Maintenance;
                        for (int k = 0; k < abilityUses.Length; k++) abilityUses[k] += g.AbilityUses[k];
                        for (int k = 0; k < maintKinds.Length; k++) maintKinds[k] += g.MaintKinds[k];
                        secondHeld += g.SecondHeld;
                        snaps += g.SnapsTaught;
                        for (int k = 0; k < cardCount; k++)
                        {
                            cardSeats[k] += g.CardSeats[k];
                            cardGold[k] += g.CardGold[k];
                        }
                    }
                    catch (Exception e)
                    {
                        errors.Add($"{seats}인 시드 {seed}: {e.GetType().Name}: {e.Message}");
                    }
                    for (int act = 0; act <= g.Act && act < reached.Length; act++) reached[act]++;
                }
                lines.Add($"  {seats}인: 명령 평균 {seatCommands / Seeds}, 라운드 평균 {seatRounds / Seeds}, 방장 멈춤 평균 {seatPause / Seeds / 60000}분");
            }
            sw.Stop();
            Report($"[M5 봇 플레이아웃] {games}/{total}게임 종막(게임 끝) — 1~{SeatId.MaxSeats}인 × 시드 {Seeds}" + (Seeds < PlayoutTests.Seeds ? "(줄인 판 — 전체 600판은 SINNOH_PLAYOUT_SEEDS=100)" : "") + $", {sw.ElapsedMilliseconds}ms, 명령 {commands}(판 최대 {maxCommands}, 라운드 최대 {maxRounds} — {maxRoundsAt}), " +
                $"Timeout {timeouts}, 포획 {captures}, 배틀 야생 {battles[(int)BattleKind.Wild]} · 트레이너 {battles[(int)BattleKind.Trainer]} · 갤럭시단 {battles[(int)BattleKind.Galactic]} · 배신자 {battles[(int)BattleKind.Traitor]} · 우두머리 {battles[(int)BattleKind.Alpha]} · 무리 {battles[(int)BattleKind.Swarm]} · 전설 {battles[(int)BattleKind.Legendary]} · 신 {battles[(int)BattleKind.God]}");
            Report($"  도달: 2막 {reached[2]}/{total} · 3막 {reached[3]}/{total} · 종막 {reached[4]}/{total} · 끝난 까닭 영웅 {reasons[(int)GameEndReason.Hero]} · 배신자 승리 {reasons[(int)GameEndReason.TraitorWon]} · 배신자 패배 {reasons[(int)GameEndReason.TraitorDefeated]}");
            Report($"  M5 제작·은행·진화(제작 대역 없음): 볼 제작 {balls} · 그 밖 제작 {crafts} · 은행상회 거래 {trades}줄 · 진화 {evolutions} · 운명 효과(디아루가) {fates} · 정비(지니기·스탬프 기술) {maint}");
            Report($"  M5 사당(고르게 고를 때 · 목표 칸 시작의 사당): 공물 {shrines} · 3장 뽑기 {shrineExtras} · 나눠 받은 축복 {shrineShared}");
            Report($"  M5 스탬프(칸별 받은 좌석 수, rulings A30): 1 포획 실패 {stamps[0]} · 2 트레이너·UB {stamps[1]} · 3 규토리 {stamps[2]} · 4 전설 포획 {stamps[3]} · 5 지닌물건 {stamps[4]} · 6 무리 {stamps[5]} · 7 운명 {stamps[6]}");
            Report($"  M5 천상의 균열·거래(rulings A31·A29): 신 강림 {descents} · 원격 도전 {remotes} · 좌석끼리 거래 {seatTrades}(봇은 제안하지 않는다)");
            Report($"  M6 트레이너 카드 해금(rulings A20-2~5 · 기본 옵션): 좌석 {seatSum} 가운데 실버에 닿음 {silver} · 골드에 닿음 {gold}");
            // M6 「봇 — 트레이너 카드」: 카드마다 게임 끝의 좌석 수·골드 좌석 수, 능력마다 AbilityUsed 수(상시 보정은 이 이벤트를 내지 않는다 — 01 §7-1). 0인 능력은 목록으로
            long used = 0, minSeats = long.MaxValue, maxSeats = 0;
            var zero = new List<string>();
            var silentZero = new List<string>();
            var noGold = new List<string>();
            var perCard = new List<string>();
            for (int k = 0; k < cardCount; k++)
            {
                string key = ctx.Cards.TrainerCards[k].Key;
                long b = abilityUses[k * 2], r = abilityUses[k * 2 + 1];
                used += b + r;
                minSeats = Math.Min(minSeats, cardSeats[k]);
                maxSeats = Math.Max(maxSeats, cardSeats[k]);
                foreach (var (n, color) in new[] { (b, " B"), (r, " R") })
                    if (n == 0) (Silent.Contains(key + color) ? silentZero : zero).Add(key + color);
                if (cardGold[k] == 0) noGold.Add(key);
                perCard.Add($"{key} {cardSeats[k]}/{cardGold[k]} B{b} R{r}");
            }
            Report($"  M6 트레이너 카드 봇(능력 사용 AbilityUsed): 합 {used} · 카드마다 좌석 {minSeats}~{maxSeats} · 골드에 못 닿은 카드 {noGold.Count}({string.Join(", ", noGold)}) · 쓴 횟수 0인 능력 {zero.Count}: {string.Join(", ", zero)}");
            Report($"    AbilityUsed를 내지 않는 능력 {Silent.Count}(상시 보정·무료 지형 등 — 풀린 좌석 수로 본다, 0인 것 {silentZero.Count}) · 그 가운데 정비로 센 것: backpacker R 둘째 지닌물건 {secondHeld} · tourist R 스냅 가르치기 {snaps}(완성 JP 30만 AbilityUsed)");
            Report($"    봇 정비 종류: {string.Join(" · ", MaintNames(maintKinds))}");
            for (int k = 0; k < perCard.Count; k += 6) Report("    " + string.Join(" · ", perCard.GetRange(k, Math.Min(6, perCard.Count - k)))); // 카드 좌석/골드 B파랑 R빨강
            foreach (var l in lines) Report(l);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.Greater(used, 0, "봇이 트레이너 카드 능력을 쓴다(M6 완료 조건 4)");
            Assert.AreEqual(SeatId.MaxSeats * Seeds, games);
            Assert.AreEqual(total, reached[4], "모든 판이 종막 카드까지");
            Assert.Greater(reasons[(int)GameEndReason.Hero], 0, "영웅으로 끝난 판");
            Assert.Greater(reasons[(int)GameEndReason.TraitorWon], 0, "배신자가 이긴 판");
            Assert.Greater(reasons[(int)GameEndReason.TraitorDefeated], 0, "배신자가 진 판");
            Assert.Greater(battles[(int)BattleKind.Traitor], 0, "배신자 연전도 나왔다");
            Assert.Greater(battles[(int)BattleKind.Wild], 0);
            Assert.Greater(battles[(int)BattleKind.Trainer], 0, "좌석끼리 도전도 나왔다");
            Assert.Greater(captures, 0, "포획도 나왔다");
            Assert.Greater(battles[(int)BattleKind.Swarm], 0, "무리 배틀도 나왔다(왜곡 게이트 찾아보기 — rulings A28 ②)");
            Assert.Greater(battles[(int)BattleKind.God], 0, "신 배틀도 나왔다(천상의 균열이 시작의 사당에 신 토큰을 놓는다 — rulings A31)");
            Assert.Greater(balls, 0, "봇이 볼을 만들어 잡는다(제작 대역 없음, §5 #33)");
            Assert.Greater(crafts, 0, "스튜·지닌물건 제작도 나왔다");
            Assert.Greater(trades, 0, "은행상회 거래도 나왔다");
            Assert.Greater(evolutions, 0, "진화도 나왔다");
            Assert.Greater(timeouts, 0, "Timeout(Default 적용)도 나왔다");
        }

        /// <summary>결정 134 ②: 새 판 기본값 트레이너 카드 「끔」 경로의 봇 판. 관문은 1~6인 × 시드 2(12판), 전체 600판 감사(SINNOH_PLAYOUT_SEEDS=100)에서는 1~6인 × 시드 100.</summary>
        [Test, Category("Slow")]
        public void M9_결정134_트레이너카드끔_봇플레이아웃종막()
        {
            int seeds = PlayoutSeeds >= Seeds ? Seeds : 2;
            var sw = Stopwatch.StartNew();
            var errors = new List<string>();
            int games = 0, ended = 0, maxRounds = 0, cards = 0, unlocked = 0;
            long used = 0;
            string maxRoundsAt = "";
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
                for (ulong seed = 0; seed < (ulong)seeds; seed++)
                {
                    var g = new Playout.Game();
                    try
                    {
                        Playout.Run(ctx, seats, seed, null, g, Campaign.HeartOfSinnoh, 0, new RuleOptions { NonPlayerMovePolicy = NonPlayerMovePolicy.Principle, TrainerCards = false });
                        games++;
                        if (g.Ended && g.Act >= HosState.Acts - 1) ended++;
                        if (g.Rounds > maxRounds) maxRoundsAt = $"{seats}인 시드 {seed}";
                        maxRounds = Math.Max(maxRounds, g.Rounds);
                        foreach (var n in g.AbilityUses) used += n;
                        foreach (var n in g.CardSeats) cards += n;
                        unlocked += g.SilverSeats + g.GoldSeats;
                    }
                    catch (Exception e)
                    {
                        errors.Add($"{seats}인 시드 {seed}: {e.GetType().Name}: {e.Message}");
                    }
                }
            Report($"[M9 결정 134 트레이너 카드 끔] {ended}/{SeatId.MaxSeats * seeds}게임 종막 — 1~{SeatId.MaxSeats}인 × 시드 {seeds}, {sw.ElapsedMilliseconds}ms, 라운드 최대 {maxRounds} — {maxRoundsAt} · 카드 좌석 {cards} · 해금 {unlocked} · 능력 사용 {used}");
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(SeatId.MaxSeats * seeds, ended, "모든 판이 종막 카드까지 가서 끝난다");
            Assert.AreEqual(0, cards, "카드를 받은 좌석이 없다");
            Assert.AreEqual(0, unlocked, "해금 없음");
            Assert.AreEqual(0, used, "트레이너 능력 사용 없음");
        }

        /// <summary>
        /// AbilityUsed를 내지 않는 능력(01 §7-1 — 상시 보정은 결과 이벤트의 Mods[]가 출처를 싣는다): 상시 보정 18(rulings A32) · 무료 지형·내려서기·닫는 턴(A36) · artist B 타입 무시 가르치기 ·
        /// dragon_tamer R · parasol_lady R · pokemon_breeder R · pokemon_ranger B·R · backpacker R(둘째 지닌물건 — 봇 정비로 센다). 봇 판 보고에서 「쓴 횟수 0」 목록에서 뺀다.
        /// </summary>
        static readonly HashSet<string> Silent = new HashSet<string>
        {
            "ace_trainer R", "artist R", "beauty B", "beauty R", "bird_keeper R", "bug_catcher R", "delinquent R", "fairy_tale_girl B", "gardener R", "hiker R",
            "poke_fan B", "roller_skater R", "ruin_maniac R", "scientist R", "sky_trainer R", "team_aqua R", "team_magma R", "youngster B",
            "hiker B", "sky_trainer B", "swimmer B", "triathlete B", "roller_skater B", "essentia B",
            "artist B", "dragon_tamer R", "parasol_lady R", "pokemon_breeder R", "pokemon_ranger B", "pokemon_ranger R", "backpacker R",
        };

        static IEnumerable<string> MaintNames(long[] counts)
        {
            for (int k = 0; k < counts.Length; k++) yield return $"{(MaintKind)k} {counts[k]}";
        }

        /// <summary>상태 크기(바이트): StateHash가 먹는 값을 센다(int 4·ulong 8).</summary>
        sealed class SizeWriter : IStateWriter
        {
            public int Bytes;
            public void Int(int value) => Bytes += 4;
            public void ULong(ulong value) => Bytes += 8;
        }

        static double MicrosPer(Action a, int n)
        {
            for (int i = 0; i < n / 10; i++) a();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) a();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds * 1000 / n;
        }

        [Test]
        public void M3_복제시간()
        {
            // 01 §2-3·§13 #12: 목표 20 µs. 넘으면 BattleSim이 필요한지 적는다. 상태 = 6인 봇 판의 배틀 중 가장 큰 상태와 게임 끝 시점 상태.
            GameState inBattle = null, atEnd = null;
            int bestSize = -1;
            Playout.Run(ctx, SeatId.MaxSeats, 3, s =>
            {
                atEnd = s;
                if (s.Battle == null) return;
                var w = new SizeWriter();
                s.Walk(w);
                if (w.Bytes > bestSize)
                {
                    bestSize = w.Bytes;
                    inBattle = s.Clone();
                }
            });
            Assert.IsNotNull(inBattle, "배틀 중 상태가 없었다");
            atEnd = atEnd.Clone();
            const int n = 20000;
            foreach (var (name, s) in new[] { ("6인 배틀 중", inBattle), ("6인 게임 끝", atEnd) })
            {
                var w = new SizeWriter();
                s.Walk(w);
                var target = s.Clone();
                double clone = MicrosPer(() => s.Clone(), n);
                double copy = MicrosPer(() => target.CopyFrom(s), n);
                double hash = MicrosPer(() => StateHash.Of(s), n / 4);
                Assert.AreEqual(StateHash.Of(s), StateHash.Of(target));
                Report($"[M3 복제 시간] {name}: 상태 {w.Bytes / 1024.0:0.0} KB(포켓몬 {s.Pokemon.Count}·토큰 {s.Tokens.Count}), Clone {clone:0.0} µs, CopyFrom(재사용) {copy:0.0} µs, StateHash {hash:0.0} µs (목표 20 µs)");
                Assert.Less(copy, 1000, "복제가 1 ms를 넘는다 — 상태 모양을 다시 본다(01 §2-3)");
            }
        }

        /// <summary>세션 판 갱신 하나를 좌석이 받은 그대로(보기는 첫 갱신만 들고 있는다).</summary>
        sealed class Got
        {
            public int Version;
            public long AnimEndMs;
            public List<GameEvent> Events;
        }

        [Test, Category("Slow")]
        public void M3_방장멈춤좌석별최댓값()
        {
            // 02 §5-7: 갱신마다 방장 멈춤 끝 = max(지금, 앞 끝) + (좌석이 받은 이벤트로 그 좌석이 센 항목 합)의 최댓값. 좌석마다 따로 배틀 맥락을 들고 센다(손님 화면 대기열처럼).
            // 봇 판 1~6인: 모든 좌석이 봇 AI(시각 = NextDueMs로 정해진 일을 하나씩), 갤럭시단은 원칙 AI.
            int updates = 0, differing = 0, games = 0;
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
                for (ulong seed = 0; seed < SessionSeeds; seed++)
                {
                    var agents = new IAgent[seats];
                    for (int i = 0; i < seats; i++) agents[i] = new Playout.BotAgent(seed);
                    var session = LocalSession.Start(ctx, new RuleOptions(), seats, seed, Campaign.HeartOfSinnoh,
                        new SessionConfig { VirtualAgent = new PrincipleAgent(), SeatAgents = agents }, 0);
                    var got = new List<Got>[seats];
                    var timing = new PlayTimingContext[seats];
                    for (int i = 0; i < seats; i++)
                    {
                        int seat = i;
                        got[i] = new List<Got>();
                        timing[i] = new PlayTimingContext(i);
                        session.Link.Connect(i, m =>
                        {
                            if (!(m is GameUpdate u)) return;
                            if (got[seat].Count == 0) timing[seat].From(u.View);
                            got[seat].Add(new Got { Version = u.Version, AnimEndMs = u.AnimEndMs, Events = u.Events });
                        });
                    }
                    long prevEnd = got[0][0].AnimEndMs;
                    int seen = 1;
                    while (session.CommandCount < SessionCommands && !session.GameOver)
                    {
                        long t = session.NextDueMs;
                        Assert.AreNotEqual(long.MaxValue, t, $"{seats}인 시드 {seed}: 봇 판이 멈췄다");
                        session.Tick(t);
                        int count = got[0].Count;
                        for (int i = 1; i < seats; i++) Assert.AreEqual(count, got[i].Count, "모든 좌석이 같은 갱신을 받는다");
                        for (int j = seen; j < count; j++)
                        {
                            int max = 0, min = int.MaxValue;
                            for (int i = 0; i < seats; i++)
                            {
                                var u = got[i][j];
                                Assert.AreEqual(got[0][j].Version, u.Version);
                                Assert.AreEqual(got[0][j].AnimEndMs, u.AnimEndMs, "멈춤 끝은 좌석마다 같은 값");
                                int ms = PlayTiming.SeatMs(u.Events, timing[i]);
                                max = Math.Max(max, ms);
                                min = Math.Min(min, ms);
                            }
                            long expected = Math.Max(t, prevEnd) + max;
                            Assert.AreEqual(expected, got[0][j].AnimEndMs, $"{seats}인 시드 {seed} 갱신 {got[0][j].Version}: 방장 멈춤 ≠ 좌석별 최댓값");
                            prevEnd = expected;
                            updates++;
                            if (min != max) differing++;
                        }
                        seen = count;
                    }
                    games++;
                }
            Report($"[M3 방장 멈춤] 봇 판 {games}개(1~6인 × 시드 {SessionSeeds}, 판마다 {SessionCommands}명령) 갱신 {updates}개, 좌석마다 길이가 다른 갱신 {differing}개");
            Assert.Greater(differing, 0, "야생전 관전 좌석처럼 좌석마다 길이가 다른 갱신이 있어야 최댓값을 시험한다");
        }
    }
}
