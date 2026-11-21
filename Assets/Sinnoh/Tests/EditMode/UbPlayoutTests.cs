using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 완료 조건 1: UB 봇 플레이아웃 1~6인 × 시드 0..PlayoutSeeds−1이 승리나 패배로 끝나고 예외 0(관문은 줄인 판, 600판 전체는 SINNOH_PLAYOUT_SEEDS=100 — 규칙 tests).
    /// 봇 정책은 PlayoutUb(⑪~⑭). 홀수 시드는 울트라 온슬롯을 켠다.
    /// </summary>
    public class UbPlayoutTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static void Report(string line)
        {
            TestContext.WriteLine(line);
            TestContext.Progress.WriteLine(line);
        }

        [Test, Category("Slow")]
        public void M6_봇플레이아웃UB()
        {
            int seeds = PlayoutTests.PlayoutSeeds;
            var sw = Stopwatch.StartNew();
            var errors = new List<string>();
            var reasons = new int[Enum.GetValues(typeof(GameEndReason)).Length];
            var all = new long[14];
            int games = 0;
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
            {
                var sum = new long[14];
                int wins = 0, ended = 0;
                long rounds = 0;
                for (ulong seed = 0; seed < (ulong)seeds; seed++)
                {
                    var g = new Playout.Game();
                    try
                    {
                        Playout.Run(ctx, seats, seed, null, g, Campaign.UltraBurst, 0, new RuleOptions { UltraOnslaught = seed % 2 == 1 });
                        if (!g.Ended) throw new InvalidOperationException("게임 끝 이벤트가 없다");
                        if (g.Reason != GameEndReason.BreachLost && g.Reason != GameEndReason.AllEliminated && g.Reason != GameEndReason.NecrozmaDefeated)
                            throw new InvalidOperationException($"UB 판이 {g.Reason}로 끝났다");
                        ended++;
                        games++;
                        reasons[(int)g.Reason]++;
                        if (g.Reason == GameEndReason.NecrozmaDefeated) wins++;
                        rounds += g.Rounds;
                    }
                    catch (Exception e)
                    {
                        errors.Add($"{seats}인 시드 {seed}: {e.GetType().Name}: {e.Message}");
                    }
                    long[] v =
                    {
                        g.Battles[(int)BattleKind.UltraBeast], g.UbDefeated, g.Battles[(int)BattleKind.Necrozma], g.NecrozmaPhases, g.Eliminations, g.Revivals,
                        g.BlessingSkips + g.BlessingRevives, g.UbTurnsSkipped, g.Battles[(int)BattleKind.Trial], g.TrialsDone, g.GuardiansDown, g.OnslaughtCards,
                        g.AbilityUses == null ? 0 : g.AbilityUses.Sum(), g.UbBattleAbilities,
                    };
                    for (int k = 0; k < v.Length; k++) sum[k] += v[k];
                }
                for (int k = 0; k < sum.Length; k++) all[k] += sum[k];
                Report($"  {seats}인: 끝 {ended}/{seeds} · 승리 {wins} · UB 배틀 {sum[0]}(격파 {sum[1]}) · 네크로즈마 도전 {sum[2]}(페이즈 격파 {sum[3]}) · 탈락 {sum[4]} · 부활 {sum[5]} · 축복 사용 {sum[6]}(건너뛴 UB 턴 {sum[7]}) · " +
                    $"시련 배틀 {sum[8]}(넘김 {sum[9]} · 수호신 {sum[10]}) · 온슬롯 카드 {sum[11]} · 트레이너 카드 능력 {sum[12]}(UB 쪽 배틀 안 {sum[13]}) · 라운드 평균 {(ended > 0 ? rounds / ended : 0)}");
            }
            sw.Stop();
            Report($"[M6 UB 봇 플레이아웃] {games}/{SeatId.MaxSeats * seeds}판 승패로 끝남 — 1~{SeatId.MaxSeats}인 × 시드 {seeds}" + (seeds < PlayoutTests.Seeds ? "(줄인 판 — 전체 600판은 SINNOH_PLAYOUT_SEEDS=100)" : "") +
                $", {sw.ElapsedMilliseconds}ms · 균열 패배 {reasons[(int)GameEndReason.BreachLost]} · 모두 탈락 {reasons[(int)GameEndReason.AllEliminated]} · 네크로즈마 격파(승리) {reasons[(int)GameEndReason.NecrozmaDefeated]}");
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(SeatId.MaxSeats * seeds, games);
            Assert.Greater(all[0], 0, "UB 배틀");
            Assert.Greater(all[1], 0, "UB 격파");
            Assert.Greater(all[2], 0, "네크로즈마 도전");
            Assert.Greater(all[4], 0, "탈락");
            Assert.Greater(all[6], 0, "축복 사용");
            Assert.Greater(all[8], 0, "호수 시련 배틀");
            Assert.Greater(all[11], 0, "온슬롯 카드(홀수 시드)");
            Assert.Greater(all[12], 0, "트레이너 카드 능력(UB 판도 기본 켬 — A20-7)");
            Assert.Greater(all[13], 0, "UB·네크로즈마·시련 배틀 안의 트레이너 카드 능력");
        }

        /// <summary>승리 길 판의 인원마다 시드 수(관문은 dotnet·Unity 모두 20 — 한 판이 짧아 줄이지 않는다, 600판 감사는 SINNOH_PLAYOUT_SEEDS=100이면 100).</summary>
        static int WinSeeds => Math.Max(20, PlayoutTests.PlayoutSeeds);

        /// <summary>
        /// 승리 길(M6 자체 검토): 위 판은 봇이 프리즘 아머 −4를 못 뚫어 네크로즈마 페이즈 격파·승리가 0이라 페이즈 2·3 · 썬/문 동전 · 3페이즈 승리 · 승리 뒤 처리가 단위 시험만으로 지켜진다.
        /// 셋업 뒤 판을 손본다(시험용 늦은 판): UB 8마리를 쓰러뜨린 것으로(토큰을 치우고 격파 8 · 프리즘 아머 울트라 버스트 쪽 1 / 울트라 비스트 쪽 8 — 네크로즈마 감소 0)
        /// · 좌석마다 빈 파티 칸을 강한 덱의 체력이 가장 큰 카드로 채운다(덱에서 빼서 — 카드 총수 보존). 봇은 격파 3 이상이라 처음부터 네크로즈마를 노린다.
        /// · 트레이너 카드를 골드로(보통 한 마리 → 강함을 카드 효과로 얻은 것처럼 — 「When unlocked」 포함, M6 「울트라 버스트 규칙 합침」): 능력 102가 UB·네크로즈마 배틀에서도 돈다.
        /// 1~6인 × 시드 <see cref="WinSeeds"/>개가 승패로 끝나고 예외 0 · 인원마다 승리 판 > 0 · 2페이즈 동전·3페이즈 도전이 실제로 돈다 · 승리 뒤 상태(3페이즈 · 토큰 없음 · 배틀 닫힘 · 열린 선택 없음).
        /// </summary>
        [Test, Category("Slow")]
        public void M6_봇플레이아웃UB승리길()
        {
            var errors = new List<string>();
            var winsBySeats = new int[SeatId.MaxSeats + 1];
            int wins = 0, phases = 0, coins = 0, thirdPhase = 0, games = 0;
            long abilities = 0, ubAbilities = 0;
            var ubBy = new long[ctx.Cards.TrainerCards.Count * 2];
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
            {
                int ph = 0;
                for (ulong seed = 0; seed < (ulong)WinSeeds; seed++)
                {
                    var g = new Playout.Game();
                    bool prepared = false, coin = false, third = false;
                    void Each(GameState st)
                    {
                        if (!prepared && st.Phase == Phase.Turn)
                        {
                            prepared = true;
                            LateGame(st, ctx);
                        }
                        if (st.Ub.SunMoon >= 0) coin = true;
                        if (st.Ub.NecrozmaPhase >= UbNecrozma.Phases) third = true;
                        if (st.Phase != Phase.GameOver || st.Ub.Result != UbResult.Won) return;
                        // 승리 뒤: 3페이즈 · 네크로즈마 토큰이 보드에 없다 · 배틀이 닫혔다 · 열린 선택이 없다
                        if (st.Ub.NecrozmaPhase != UbNecrozma.Phases) throw new InvalidOperationException($"승리인데 페이즈 {st.Ub.NecrozmaPhase}");
                        if (st.Tokens.Exists(t => t.Kind == TokenKind.Necrozma && t.Cell >= 0)) throw new InvalidOperationException("승리 뒤에도 네크로즈마 토큰이 보드에 있다");
                        if (st.Battle != null) throw new InvalidOperationException("승리 뒤에도 배틀이 열려 있다");
                        if (st.Pending.Exists(c => !c.Answered)) throw new InvalidOperationException("승리 뒤에도 열린 선택이 있다");
                    }
                    try
                    {
                        Playout.Run(ctx, seats, seed, Each, g, Campaign.UltraBurst, 0, new RuleOptions { UltraOnslaught = seed % 2 == 1 });
                        if (!g.Ended) throw new InvalidOperationException("게임 끝 이벤트가 없다");
                        if (g.Reason != GameEndReason.BreachLost && g.Reason != GameEndReason.AllEliminated && g.Reason != GameEndReason.NecrozmaDefeated)
                            throw new InvalidOperationException($"UB 판이 {g.Reason}로 끝났다");
                        games++;
                        if (g.Reason == GameEndReason.NecrozmaDefeated)
                        {
                            wins++;
                            winsBySeats[seats]++;
                        }
                    }
                    catch (Exception e)
                    {
                        errors.Add($"{seats}인 시드 {seed}: {e.GetType().Name}: {e.Message}");
                    }
                    ph += g.NecrozmaPhases;
                    if (g.AbilityUses != null) abilities += g.AbilityUses.Sum();
                    ubAbilities += g.UbBattleAbilities;
                    if (g.UbAbilityUses != null)
                        for (int k = 0; k < ubBy.Length; k++) ubBy[k] += g.UbAbilityUses[k];
                    if (coin) coins++;
                    if (third) thirdPhase++;
                }
                phases += ph;
                Report($"  {seats}인: 승리 {winsBySeats[seats]}/{WinSeeds} · 페이즈 격파 {ph}");
            }
            Report($"[M6 UB 승리 길] {games}/{SeatId.MaxSeats * WinSeeds}판 승패로 끝남 · 승리 {wins} · 페이즈 격파 {phases} · 동전 던진 판 {coins} · 3페이즈에 닿은 판 {thirdPhase} · 트레이너 카드 능력 {abilities}(UB 쪽 배틀 안 {ubAbilities})");
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(SeatId.MaxSeats * WinSeeds, games);
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++) Assert.Greater(winsBySeats[seats], 0, $"{seats}인 승리 판");
            Assert.Greater(coins, 0, "썬/문 동전");
            Assert.Greater(thirdPhase, 0, "3페이즈");
            var named = new List<string>();
            for (int k = 0; k < ubBy.Length; k++)
                if (ubBy[k] > 0) named.Add($"{ctx.Cards.TrainerCardIds[k / 2]} {(k % 2 == 0 ? "B" : "R")} {ubBy[k]}");
            Report($"  UB 쪽 배틀 안에서 쓴 능력 {named.Count}가지: {string.Join(" · ", named)}");
            Assert.Greater(ubAbilities, 0, "골드 트레이너 카드의 능력이 UB·네크로즈마·시련 배틀 안에서");
        }

        /// <summary>승리 길 판의 손보기(위 시험 설명): UB 8마리 격파 상태 · 파티 빈 칸을 강한 덱의 큰 카드로.</summary>
        static void LateGame(GameState s, Ctx ctx)
        {
            int left = 1;
            foreach (var t in s.Tokens)
            {
                if (t.Kind != TokenKind.UltraBeast || t.Cell < 0) continue;
                if (left-- > 0) continue;
                t.Cell = -1;
                s.Ub.Defeated++;
                s.Ub.PrismUltraBurst--;
                s.Ub.PrismUltraBeast++;
            }
            var strong = s.Decks[(int)DeckId.Strong];
            var moderate = s.Decks[(int)DeckId.Moderate];
            for (int i = 0; i < s.SeatCount; i++)
            {
                var seat = s.Seats[i];
                int fresh = -1;
                if (seat.TrainerCard >= 0 && moderate.Count > 0 && PokemonRules.PartyCount(seat) < seat.OpenSlots)
                {
                    // 보통 한 마리를 얻어 실버(파랑)
                    int m = moderate[moderate.Count - 1];
                    moderate.RemoveAt(moderate.Count - 1);
                    int p = PokemonRules.Spawn(s, m, i, (int)DeckId.Moderate);
                    PokemonRules.AddToParty(seat, p);
                    TrainerRules.Obtained(s, ctx, Scene.Fx(s, null, i), i, p, ObtainCause.CardEffect);
                }
                while (PokemonRules.PartyCount(seat) < seat.OpenSlots)
                {
                    int at = 0;
                    for (int k = 1; k < strong.Count; k++)
                        if (ctx.Cards.Pokemon[strong[k]].Health > ctx.Cards.Pokemon[strong[at]].Health) at = k;
                    int card = strong[at];
                    strong.RemoveAt(at);
                    int p = PokemonRules.Spawn(s, card, i, (int)DeckId.Strong);
                    PokemonRules.AddToParty(seat, p);
                    if (fresh < 0) fresh = p;
                }
                if (fresh >= 0 && seat.TrainerCard >= 0) TrainerRules.Obtained(s, ctx, Scene.Fx(s, null, i), i, fresh, ObtainCause.CardEffect); // 강함을 얻어 골드(빨강)
                // poke_maniac R이 받은 대타출동은 봇이 지니게 하지 않는다(UB 판에는 막이 없어 봇 ⑨가 돌지 않는다) — 둘째 칸 포켓몬에게 쥐여 준다
                int sub = ctx.Items.Ids.IndexOf("item.substitute");
                if (sub >= 0 && seat.Party[1] >= 0 && s.Pokemon[seat.Party[1]].HeldItem < 0 && seat.Bag.Remove(sub)) s.Pokemon[seat.Party[1]].HeldItem = sub;
            }
        }
    }
}
